namespace Openctrol.Agent.Logging;

public sealed class FileLogger : ILogger
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public FileLogger()
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            _logDirectory = Path.Combine(programData, "Openctrol", "logs");
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            // If we can't create the log directory, use a fallback location
            try
            {
                _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Openctrol", "logs");
                Directory.CreateDirectory(_logDirectory);
            }
            catch
            {
                // Last resort: use temp directory
                _logDirectory = Path.Combine(Path.GetTempPath(), "Openctrol", "logs");
                Directory.CreateDirectory(_logDirectory);
            }
        }
    }

    private string GetLogFilePath()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"agent-{today}.log");
    }

    public void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public void Warn(string message)
    {
        WriteLog("WARN", message);
    }

    public void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}: {ex}" : message;
        WriteLog("ERROR", fullMessage);
    }

    public void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }

    private void WriteLog(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var logFile = GetLogFilePath();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                
                // Ensure directory exists (in case it was deleted)
                var logDir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                
                File.AppendAllText(logFile, logLine);
            }
            catch (UnauthorizedAccessException)
            {
                // Try to write to Event Log as fallback
                try
                {
                    System.Diagnostics.EventLog.WriteEntry("OpenctrolAgent", $"[{level}] {message}", 
                        level == "ERROR" ? System.Diagnostics.EventLogEntryType.Error :
                        level == "WARN" ? System.Diagnostics.EventLogEntryType.Warning :
                        System.Diagnostics.EventLogEntryType.Information);
                }
                catch
                {
                    // If Event Log also fails, silently fail
                }
            }
            catch
            {
                // Silently fail if logging fails
            }
        }
    }
}

