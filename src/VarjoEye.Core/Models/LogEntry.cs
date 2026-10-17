namespace VarjoEye.Core.Models;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);
