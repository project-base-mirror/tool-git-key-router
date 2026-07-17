namespace GitKeyRouter.Core.Models;

public sealed class ProcessRequest
{
    public required string ExecutablePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; }
        = new Dictionary<string, string?>();

    public bool CreateNoWindow { get; init; } = true;
}
