namespace Openctrol.Agent.Logging;

public sealed class FileLogger : ILogger
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public FileLogger()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _logDirectory = Path.Combine(programData, "Openctrol", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    private string GetLogFilePath()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"openctrol-{today}.log");
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

    private void WriteLog(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var logFile = GetLogFilePath();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, logLine);
            }
            catch
            {
                // Silently fail if logging fails
            }
        }
    }
}

