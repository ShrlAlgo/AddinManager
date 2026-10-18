using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace AddInManager.DebugTools
{
    /// <summary>
    /// Singleton logger that collects runtime and debugging information
    /// at various stages of the application lifecycle.
    /// </summary>
    public sealed class DebugLogger
    {
        private static readonly Lazy<DebugLogger> _instance =
            new Lazy<DebugLogger>(() => new DebugLogger());

        [ThreadStatic]
        private static bool _suppressDiagnosticsCapture;

        [ThreadStatic]
        private static bool _capturingException;

        private readonly Dispatcher _dispatcher;
        private readonly TraceListener _diagnosticsListener;
        private readonly object _fileLock = new object();
        private readonly Dictionary<TraceSource, SourceLevels> _wpfTraceLevels = new Dictionary<TraceSource, SourceLevels>();
        private bool _captureStarted;
        private string _lastCommandFilePath;
        private string _lastCommandClassName;

        internal string LogFilePath { get; }

        public static DebugLogger Instance => _instance.Value;

        /// <summary>All captured log entries (observable for WPF binding).</summary>
        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>Raised whenever a new entry is added.</summary>
        public event EventHandler<LogEntry> EntryAdded;

        private DebugLogger()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AddInManager", "Logs", $"Debug_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Process.GetCurrentProcess().Id}.log");
            _diagnosticsListener = new DiagnosticsTraceListener(this);
            Trace.Listeners.Add(_diagnosticsListener);
        }

        // WPF 绑定和资源诊断有独立的 TraceSource，不经过 Trace.Listeners。
        internal void StartCapture()
        {
            if (_captureStarted) return;
            _captureStarted = true;
            foreach (var source in new[] { PresentationTraceSources.DataBindingSource,
                PresentationTraceSources.ResourceDictionarySource, PresentationTraceSources.MarkupSource })
            {
                _wpfTraceLevels.Add(source, source.Switch.Level);
            }
            // 未附加调试器时，WPF 仍需显式启用自身的跟踪入口。
            PresentationTraceSources.Refresh();
            if (!Trace.Listeners.Contains(_diagnosticsListener)) Trace.Listeners.Add(_diagnosticsListener);
            foreach (var entry in _wpfTraceLevels)
            {
                entry.Key.Switch.Level = entry.Value | SourceLevels.Warning;
                entry.Key.Listeners.Add(_diagnosticsListener);
            }
            _dispatcher.UnhandledException += Dispatcher_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            Info($"Diagnostic log: {LogFilePath}");
        }

        internal void StopCapture()
        {
            if (!_captureStarted) return;
            AppDomain.CurrentDomain.FirstChanceException -= CurrentDomain_FirstChanceException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            _dispatcher.UnhandledException -= Dispatcher_UnhandledException;
            foreach (var entry in _wpfTraceLevels)
            {
                entry.Key.Listeners.Remove(_diagnosticsListener);
                if (entry.Key.Switch.Level == (entry.Value | SourceLevels.Warning)) entry.Key.Switch.Level = entry.Value;
            }
            _wpfTraceLevels.Clear();
            Trace.Listeners.Remove(_diagnosticsListener);
            _captureStarted = false;
        }

        // 在加载程序集前记录入口，加载失败时仍保留命令上下文。
        internal void RecordCommandContext(string filePath, string fullClassName)
        {
            _lastCommandFilePath = filePath;
            _lastCommandClassName = fullClassName;
        }

        internal string CreateDiagnosticReport(string revitVersion)
        {
            var lastWriteTime = "-";
            try
            {
                if (File.Exists(_lastCommandFilePath)) lastWriteTime = File.GetLastWriteTimeUtc(_lastCommandFilePath).ToString("O");
            }
            catch (Exception ex)
            {
                lastWriteTime = ex.Message;
            }

            // 报告不受界面筛选影响，保留最近 200 条诊断记录及完整异常链。
            var log = string.Join(Environment.NewLine, Entries.Skip(Math.Max(0, Entries.Count - 200)).Select(entry => entry.ToString()));
            return string.Format(Properties.Resources.DiagnosticReportFormat,
                DateTimeOffset.Now.ToString("O"), typeof(DebugLogger).Assembly.GetName().Version,
                revitVersion ?? "-", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, Environment.Is64BitProcess,
                _lastCommandClassName ?? "-", _lastCommandFilePath ?? "-", lastWriteTime, LogFilePath, log);
        }

        private void Dispatcher_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            CaptureException(e.Exception, "WPF DispatcherUnhandledException");
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            CaptureException(e.Exception, "Task UnobservedTaskException");
        }

        // 只观察 XAML、资源和 Revit API 异常；FirstChance 不代表异常最终未被处理。
        private void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            var exception = e.Exception;
            var exceptionType = exception.GetType();
            if (exception is XamlParseException || exception is ResourceReferenceKeyNotFoundException ||
                exceptionType.FullName == "System.Xaml.XamlParseException" ||
                exceptionType.Namespace?.StartsWith("Autodesk.Revit.Exceptions", StringComparison.Ordinal) == true)
            {
                CaptureException(exception, "FirstChance (may be handled)", LogLevel.Warning);
            }
        }

        private void CaptureException(Exception exception, string context, LogLevel level = LogLevel.Error)
        {
            if (_capturingException) return;
            _capturingException = true;
            try
            {
                Log(level, $"{context}; thread {System.Threading.Thread.CurrentThread.ManagedThreadId} — {exception}");
            }
            catch
            {
                // 异常观察器自身的错误不再向宿主传播。
            }
            finally
            {
                _capturingException = false;
            }
        }

        private void AddEntry(LogEntry entry, bool mirrorToSystemDiagnostics)
        {
            // 先落盘，再更新 UI；崩溃线程不等待 UI Dispatcher。
            WriteToFile(entry);
            RunOnDispatcher(() =>
            {
                Entries.Add(entry);
                EntryAdded?.Invoke(this, entry);
            });

            if (mirrorToSystemDiagnostics)
            {
                MirrorToSystemDiagnostics(entry.ToString());
            }
        }

        private void WriteToFile(LogEntry entry)
        {
            try
            {
                lock (_fileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath));
                    File.AppendAllText(LogFilePath, entry + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // 日志目录不可写时仍保留内存日志。
            }
        }

        private void RunOnDispatcher(Action action)
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            if (_dispatcher != null && !_dispatcher.CheckAccess())
            {
                _dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }

        private void MirrorToSystemDiagnostics(string message)
        {
            try
            {
                _suppressDiagnosticsCapture = true;
                Debug.WriteLine(message);
            }
            finally
            {
                _suppressDiagnosticsCapture = false;
            }
        }

        private void CaptureSystemDiagnostics(string message, LogLevel level = LogLevel.Info)
        {
            if (_suppressDiagnosticsCapture || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            AddEntry(new LogEntry(level, "System.Diagnostics", message), false);
        }

        public void Log(LogLevel level, string message)
        {
            AddEntry(new LogEntry(level, "DebugLogger", message), true);
        }

        public void Info(string message) => Log(LogLevel.Info, message);

        public void Warning(string message) => Log(LogLevel.Warning, message);

        public void Error(string message) => Log(LogLevel.Error, message);

        public void Error(Exception ex, string context = null)
        {
            var msg = string.IsNullOrEmpty(context)
                ? ex.ToString()
                : $"{context} — {ex}";
            Log(LogLevel.Error, msg);
        }

        public void Clear()
        {
            RunOnDispatcher(() => Entries.Clear());
        }

        private sealed class DiagnosticsTraceListener : TraceListener
        {
            private readonly DebugLogger _owner;
            private readonly StringBuilder _buffer = new StringBuilder();
            private readonly object _syncRoot = new object();

            public DiagnosticsTraceListener(DebugLogger owner)
            {
                _owner = owner;
            }

            public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
            {
                var level = (eventType & (TraceEventType.Error | TraceEventType.Critical)) != 0 ? LogLevel.Error
                    : eventType == TraceEventType.Warning ? LogLevel.Warning : LogLevel.Info;
                _owner.CaptureSystemDiagnostics($"{source} [{id}]: {message}", level);
            }

            public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
            {
                TraceEvent(eventCache, source, eventType, id, args == null ? format : string.Format(format, args));
            }

            public override void Write(string message)
            {
                if (_suppressDiagnosticsCapture || string.IsNullOrEmpty(message))
                {
                    return;
                }

                lock (_syncRoot)
                {
                    _buffer.Append(message);
                    FlushCompleteLines();
                }
            }

            public override void WriteLine(string message)
            {
                if (_suppressDiagnosticsCapture)
                {
                    return;
                }

                lock (_syncRoot)
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        _buffer.Append(message);
                    }

                    FlushRemainingBuffer();
                }
            }

            private void FlushCompleteLines()
            {
                while (true)
                {
                    var text = _buffer.ToString();
                    var lineFeedIndex = text.IndexOf('\n');
                    if (lineFeedIndex < 0)
                    {
                        return;
                    }

                    var line = text.Substring(0, lineFeedIndex).TrimEnd('\r');
                    _buffer.Remove(0, lineFeedIndex + 1);
                    _owner.CaptureSystemDiagnostics(line);
                }
            }

            private void FlushRemainingBuffer()
            {
                FlushCompleteLines();
                if (_buffer.Length <= 0)
                {
                    return;
                }

                var line = _buffer.ToString().TrimEnd('\r');
                _buffer.Clear();
                _owner.CaptureSystemDiagnostics(line);
            }
        }
    }
}
