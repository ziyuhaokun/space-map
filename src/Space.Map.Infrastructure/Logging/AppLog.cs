using System.IO;

namespace Space.Map.Infrastructure.Logging;

public static class AppLog
{
    /// <summary>
    /// How many log files to keep. One file is written per run, so this bounds the
    /// log directory to the last few launches.
    /// </summary>
    private const int RetainedLogFiles = 10;

    private static readonly object SyncRoot = new();
    private static FileLogger? _logger;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Space.Map",
        "logs");

    public static string? CurrentLogFile => _logger?.FilePath;

    public static void Initialize()
    {
        // Two threads can reach logging at once (a scan worker reporting while the
        // UI logs). Without the lock each builds its own FileLogger: the second
        // CreateNew on the same timestamped name throws, and the loser's stream is
        // never disposed, leaking a handle that keeps the file open.
        lock (SyncRoot)
        {
            _logger ??= new FileLogger(LogDirectory);
        }
    }

    public static void Trace(string message) => Write(LogLevel.Trace, message);

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Information(string message) => Write(LogLevel.Information, message);

    public static void Warning(string message) => Write(LogLevel.Warning, message);

    public static void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    public static void Critical(string message, Exception? exception = null) => Write(LogLevel.Critical, message, exception);

    public static void Shutdown()
    {
        FileLogger? logger;
        lock (SyncRoot)
        {
            logger = _logger;
            _logger = null;
        }

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

    /// <summary>
    /// Deletes the oldest logs beyond <see cref="RetainedLogFiles"/>. Runs once per
    /// launch, after the new file exists, so the directory never grows unbounded.
    /// </summary>
    public static void TrimOldLogs()
    {
        try
        {
            var files = new DirectoryInfo(LogDirectory).GetFiles("disk-space-visualizer-*.log");
            if (files.Length <= RetainedLogFiles)
            {
                return;
            }

            Array.Sort(files, static (left, right) => right.CreationTimeUtc.CompareTo(left.CreationTimeUtc));
            for (var index = RetainedLogFiles; index < files.Length; index++)
            {
                try
                {
                    files[index].Delete();
                }
                catch
                {
                    // A log open in another instance is not worth failing over.
                }
            }
        }
        catch
        {
            // Retention is housekeeping; never surface it.
        }
    }
}
