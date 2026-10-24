using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RentRight.Tests.Diagnostics;

public sealed record CapturedLog(string Category, LogLevel Level, EventId Event,
    string Message, IReadOnlyDictionary<string, object?> Fields, Exception? Exception);

public sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);
    public void Dispose() { }
    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new(category, level, eventId, formatter(state, exception),
                state is IEnumerable<KeyValuePair<string, object?>> fields ? fields.ToDictionary() : new Dictionary<string, object?>(), exception));
    }
}
