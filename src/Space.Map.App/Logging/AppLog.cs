namespace Space.Map.App.Logging;

using System.IO;

public static class AppLog
{
    private static FileLogger? _logger;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Space.Map",
        "logs");

    public static string? CurrentLogFile => _logger?.FilePath;

    public static void Initialize()
    {
        _logger ??= new FileLogger(LogDirectory);
    }

    public static void Trace(string message) => Write(LogLevel.Trace, message);

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Information(string message) => Write(LogLevel.Information, message);

    public static void Warning(string message) => Write(LogLevel.Warning, message);

    public static void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    public static void Critical(string message, Exception? exception = null) => Write(LogLevel.Critical, message, exception);

    public static void Shutdown()
    {
        var logger = _logger;
        _logger = null;
        logger?.Dispose();
    }

    private static void Write(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            Initialize();
            _logger?.Write(level, message, exception);
        }
        catch
        {
            // Logging must never become the reason the application cannot start.
        }
    }
}
