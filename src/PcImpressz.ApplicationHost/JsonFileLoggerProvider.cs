using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PcImpressz.ApplicationHost;

internal sealed class JsonFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            var current = Path.Combine(directory, "core.jsonl");
            if (File.Exists(current) && new FileInfo(current).Length > 5 * 1024 * 1024)
            {
                for (var i = 2; i >= 1; i--)
                {
                    var source = i == 1 ? current : Path.Combine(directory, $"core.{i - 1}.jsonl");
                    if (File.Exists(source)) File.Move(source, Path.Combine(directory, $"core.{i}.jsonl"), overwrite: true);
                }
            }
            var line = JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow, level = level.ToString(), category, message,
                errorType = exception?.GetType().Name
            });
            File.AppendAllText(current, line + Environment.NewLine);
        }
    }

    private sealed class FileLogger(JsonFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
