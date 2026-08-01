using Microsoft.Extensions.Logging;

namespace Minidisp.Companion.Logging;

/// <summary>
/// Minimal structured file logger (a tray app has no console). Writes
/// "timestamp level category message" lines to %LOCALAPPDATA%\Minidisp\logs.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(LogLevel minLevel = LogLevel.Information)
    {
        _minLevel = minLevel;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Minidisp", "logs");
        Directory.CreateDirectory(dir);
        _writer = new StreamWriter(
            new FileStream(Path.Combine(dir, "companion.log"), FileMode.Append,
                FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write(string line)
    {
        lock (_gate) _writer.WriteLine(line);
    }

    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] " +
                       $"{category}: {formatter(state, exception)}";
            if (exception is not null) line += $" | {exception}";
            provider.Write(line);
        }
    }
}
