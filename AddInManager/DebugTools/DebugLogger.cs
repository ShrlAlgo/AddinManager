using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
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

        private readonly Dispatcher _dispatcher;
        private readonly TraceListener _diagnosticsListener;

        public static DebugLogger Instance => _instance.Value;

        /// <summary>All captured log entries (observable for WPF binding).</summary>
        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>Raised whenever a new entry is added.</summary>
        public event EventHandler<LogEntry> EntryAdded;

        private DebugLogger()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _diagnosticsListener = new DiagnosticsTraceListener(this);
            Trace.Listeners.Add(_diagnosticsListener);
        }

        private void AddEntry(LogEntry entry, bool mirrorToSystemDiagnostics)
        {
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

        private void RunOnDispatcher(Action action)
        {
            if (_dispatcher != null && !_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(action);
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

        private void CaptureSystemDiagnostics(string message)
        {
            if (_suppressDiagnosticsCapture || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            AddEntry(new LogEntry(LogLevel.Info, "System.Diagnostics", message), false);
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
                ? $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
                : $"{context} — {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
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
