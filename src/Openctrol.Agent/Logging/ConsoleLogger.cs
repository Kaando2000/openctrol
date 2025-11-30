namespace Openctrol.Agent.Logging;

public sealed class ConsoleLogger : ILogger
{
    public void Info(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }

    public void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] {message}");
        Console.ResetColor();
    }

    public void Error(string message, Exception? ex = null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        var fullMessage = ex != null ? $"{message}: {ex}" : message;
        Console.WriteLine($"[ERROR] {fullMessage}");
        Console.ResetColor();
    }

    public void Debug(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[DEBUG] {message}");
        Console.ResetColor();
    }
}

