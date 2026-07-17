namespace GitKeyRouter.Core.Models;

public sealed class ProcessResult
{
    public required string ExecutablePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public int? ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public bool Cancelled { get; init; }

    public TimeSpan Duration { get; init; }

    public Exception? StartException { get; init; }

    public bool Started => StartException is null;

    public bool Succeeded => Started && !TimedOut && !Cancelled && ExitCode == 0;
}
