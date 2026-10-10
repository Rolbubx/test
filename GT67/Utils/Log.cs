namespace GT67.Utils;

/// <summary>Minimal thread-safe console logger.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Debug(string message) => Write("DEBUG", message, ConsoleColor.DarkGray);
    public static void Info(string message) => Write("INFO ", message, ConsoleColor.Gray);
    public static void Warn(string message) => Write("WARN ", message, ConsoleColor.Yellow);
    public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

    private static void Write(string level, string message, ConsoleColor color)
    {
        lock (Gate)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
            Console.ForegroundColor = previous;
        }
    }
}
