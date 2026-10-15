using Microsoft.Extensions.Logging;

namespace VTMF.Settings.Entities;

public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel LogLevel { get; init; }
    public string Message { get; init; } = string.Empty;
}
