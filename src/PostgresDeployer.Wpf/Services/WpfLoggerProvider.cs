namespace PostgresDeployer.Wpf.Services;

using Microsoft.Extensions.Logging;

public class WpfLoggerProvider : ILoggerProvider
{
    private readonly Action<string, LogLevel> _logAction;

    public WpfLoggerProvider(Action<string, LogLevel> logAction)
    {
        _logAction = logAction;
    }

    public ILogger CreateLogger(string categoryName)
        => new WpfLogger(_logAction);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private class WpfLogger : ILogger
    {
        private readonly Action<string, LogLevel> _logAction;

        public WpfLogger(Action<string, LogLevel> logAction)
        {
            _logAction = logAction;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _logAction($"[{DateTime.Now:HH:mm:ss}] [{logLevel}] {message}", logLevel);
        }
    }
}
