using Microsoft.Extensions.Logging;
using ILogger = Openctrol.Agent.Logging.ILogger;

namespace Openctrol.Agent.Hosting;

/// <summary>
/// Adapter that wraps Openctrol.Agent.Logging.ILogger to Microsoft.Extensions.Logging.ILogger
/// </summary>
public sealed class LoggerAdapter : Microsoft.Extensions.Logging.ILogger
{
    private readonly ILogger _innerLogger;

    public LoggerAdapter(ILogger innerLogger)
    {
        _innerLogger = innerLogger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        
        switch (logLevel)
        {
            case LogLevel.Information:
            case LogLevel.Trace:
            case LogLevel.Debug:
                _innerLogger.Info(message);
                break;
            case LogLevel.Warning:
                _innerLogger.Warn(message);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                _innerLogger.Error(message, exception);
                break;
        }
    }
}

