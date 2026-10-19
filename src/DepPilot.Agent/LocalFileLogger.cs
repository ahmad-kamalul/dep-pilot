using Microsoft.Extensions.Logging;

namespace DepPilot.Agent;

// Small rotating operational log; Ansible output is separately redacted by the runner.
public sealed class LocalFileLogger(string directory) : ILoggerProvider
{
    private readonly object sync = new();
    public ILogger CreateLogger(string categoryName) => new FileLog(this, categoryName);
    public void Dispose() { }
    private void Write(string message)
    {
        lock (sync)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "agent.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".old", true);
            File.AppendAllText(path, message + Environment.NewLine);
        }
    }
    private sealed class FileLog(LocalFileLogger owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) owner.Write($"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state, exception)}");
        }
    }
}
