using System.IO;
using System.Text;

namespace Space.Map.Infrastructure.Logging;

public sealed class FileLogger : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        var fileName = $"disk-space-visualizer-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log";
        FilePath = Path.Combine(logDirectory, fileName);

        var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public string FilePath { get; }

    public void Trace(string message) => Write(LogLevel.Trace, message);

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Information(string message) => Write(LogLevel.Information, message);

    public void Warning(string message) => Write(LogLevel.Warning, message);

    public void Error(string message, Exception? exception = null) => Write(LogLevel.Error, message, exception);

    public void Critical(string message, Exception? exception = null) => Write(LogLevel.Critical, message, exception);

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _writer.Write(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            _writer.Write(" [");
            _writer.Write(level);
            _writer.Write("] ");
            _writer.WriteLine(message);

            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
