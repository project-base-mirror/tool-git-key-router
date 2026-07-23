using System.Text;
using GitKeyRouter.Core.Abstractions;

namespace GitKeyRouter.Infrastructure.Logging;

public sealed class SafeFileLogger : ISafeLogger
{
    public const long DefaultMaxFileBytes = 5 * 1024 * 1024;
    public const int DefaultRetainedFileCount = 3;

    private readonly string _logPath;
    private readonly long _maxFileBytes;
    private readonly int _retainedFileCount;
    private readonly object _sync = new();

    public SafeFileLogger(
        IAppPaths paths,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFileCount = DefaultRetainedFileCount)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }

        if (retainedFileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFileCount));
        }

        _logPath = Path.Combine(paths.AppDataDirectory, "gitkeyrouter.log");
        _maxFileBytes = maxFileBytes;
        _retainedFileCount = retainedFileCount;
    }

    public void Information(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var safeMessage = SensitiveDataRedactor.Redact(message);
        var safeException = exception is null
            ? string.Empty
            : Environment.NewLine + SensitiveDataRedactor.Redact(exception.ToString());
        var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{safeException}{Environment.NewLine}";

        lock (_sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(_logPath)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
            catch (Exception fileException) when (IsRecoverableFileFailure(fileException))
            {
                // Logging must never make the primary operation fail.
            }
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_logPath))
        {
            return;
        }

        var currentLength = new FileInfo(_logPath).Length;
        if (currentLength + incomingBytes <= _maxFileBytes)
        {
            return;
        }

        if (_retainedFileCount == 0)
        {
            File.Delete(_logPath);
            return;
        }

        for (var index = _retainedFileCount; index >= 1; index--)
        {
            var destination = $"{_logPath}.{index}";
            var source = index == 1 ? _logPath : $"{_logPath}.{index - 1}";
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private static bool IsRecoverableFileFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException;
}
