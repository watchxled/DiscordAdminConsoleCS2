namespace DiscordAdminConsole.Logging;

public static class Log
{
    private static volatile bool _debug;

    public static void Configure(bool debug) => _debug = debug;

    public static void Debug(string message)
    {
        if (_debug)
            Write("DEBUG", message, ConsoleColor.DarkGray);
    }

    public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);

    public static void Warning(string message) => Write("WARN", message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("ERROR", message, ConsoleColor.Red);

    private static void Write(string level, string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[DiscordAdmin][{level}] {message}");
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
