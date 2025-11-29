using System.Diagnostics;

namespace Openctrol.Agent.Logging;

public sealed class EventLogLogger : ILogger
{
    private const string SourceName = "OpenctrolAgent";
    private readonly EventLog? _eventLog;

    public EventLogLogger()
    {
        try
        {
            if (!EventLog.SourceExists(SourceName))
            {
                EventLog.CreateEventSource(SourceName, "Application");
            }
            _eventLog = new EventLog("Application", ".", SourceName);
        }
        catch
        {
            // Event log may not be available, continue without it
            _eventLog = null;
        }
    }

    public void Info(string message)
    {
        _eventLog?.WriteEntry(message, EventLogEntryType.Information);
    }

    public void Warn(string message)
    {
        _eventLog?.WriteEntry(message, EventLogEntryType.Warning);
    }

    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}: {ex}" : message;
        _eventLog?.WriteEntry(fullMessage, EventLogEntryType.Error);
    }

    public void Debug(string message)
    {
        // Event log doesn't have a debug level, use Information
        _eventLog?.WriteEntry(message, EventLogEntryType.Information);
    }
}

