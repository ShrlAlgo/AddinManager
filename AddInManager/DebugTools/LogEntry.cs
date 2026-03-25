using System;

namespace AddInManager.DebugTools
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Source { get; }
        public string Message { get; }

        public LogEntry(LogLevel level, string message)
            : this(level, "DebugLogger", message)
        {
        }

        public LogEntry(LogLevel level, string source, string message)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            Message = message;
        }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] [{Level}] [{Source}] {Message}";
        }
    }
}
