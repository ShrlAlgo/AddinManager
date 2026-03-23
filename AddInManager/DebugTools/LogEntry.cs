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
        public string Message { get; }

        public LogEntry(LogLevel level, string message)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Message = message;
        }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
        }
    }
}
