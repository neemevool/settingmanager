using Microsoft.Extensions.Logging;
using VTMF.Settings.Entities;

namespace VTMF.Settings.Services;

public class ListLogger(string categoryName, List<LogEntry> logEntries) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += Environment.NewLine + exception;

        logEntries.Add(new LogEntry
        {
            LogLevel = logLevel,
            Message = $"[{categoryName}] {message}"
        });
    }
}

public class ListLoggerFactory(List<LogEntry> logEntries) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, logEntries);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
