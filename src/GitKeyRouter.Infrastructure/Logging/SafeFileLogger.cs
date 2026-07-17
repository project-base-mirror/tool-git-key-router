using GitKeyRouter.Core.Abstractions;

namespace GitKeyRouter.Infrastructure.Logging;

public sealed class SafeFileLogger : ISafeLogger
{
    private readonly string _logPath;
    private readonly object _sync = new();

    public SafeFileLogger(IAppPaths paths)
    {
        _logPath = Path.Combine(paths.AppDataDirectory, "gitkeyrouter.log");
    }

    public void Information(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var safeMessage = SensitiveDataRedactor.Redact(message);
        var safeException = exception is null ? string.Empty : Environment.NewLine + SensitiveDataRedactor.Redact(exception.ToString());
        var line = $"{DateTimeOffset.Now:O} [{level}] {safeMessage}{safeException}{Environment.NewLine}";
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(_logPath, line);
        }
    }
}
