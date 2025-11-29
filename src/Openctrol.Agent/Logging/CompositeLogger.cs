namespace Openctrol.Agent.Logging;

public sealed class CompositeLogger : ILogger
{
    private readonly ILogger[] _loggers;

    public CompositeLogger(params ILogger[] loggers)
    {
        _loggers = loggers;
    }

    public void Info(string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.Info(message);
            }
            catch
            {
                // Continue with other loggers if one fails
            }
        }
    }

    public void Warn(string message)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.Warn(message);
            }
            catch
            {
                // Continue with other loggers if one fails
            }
        }
    }

    public void Error(string message, Exception? ex = null)
    {
        foreach (var logger in _loggers)
        {
            try
            {
                logger.Error(message, ex);
            }
            catch
            {
                // Continue with other loggers if one fails
            }
        }
    }
}

