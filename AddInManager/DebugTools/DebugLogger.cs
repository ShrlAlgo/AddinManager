using System;
using System.Collections.ObjectModel;

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

        public static DebugLogger Instance => _instance.Value;

        /// <summary>All captured log entries (observable for WPF binding).</summary>
        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>Raised on the calling thread whenever a new entry is added.</summary>
        public event EventHandler<LogEntry> EntryAdded;

        private DebugLogger() { }

        private void AddEntry(LogEntry entry)
        {
            Entries.Add(entry);
            EntryAdded?.Invoke(this, entry);
            System.Diagnostics.Debug.WriteLine(entry.ToString());
        }

        public void Log(LogLevel level, string message)
        {
            AddEntry(new LogEntry(level, message));
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
            Entries.Clear();
        }
    }
}
