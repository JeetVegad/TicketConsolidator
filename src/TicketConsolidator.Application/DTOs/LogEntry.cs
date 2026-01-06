using System;

namespace TicketConsolidator.Application.DTOs
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel Level { get; set; }

        public LogEntry(string message, LogLevel level)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
        }
    }
}
