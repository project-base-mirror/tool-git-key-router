namespace GitKeyRouter.Core.Models;

public sealed class GitServiceConnectionResult
{
    public required GitServiceInstance Service { get; init; }
    public required ProcessResult Process { get; init; }
    public bool Authenticated { get; init; }
    public string Classification { get; init; } = string.Empty;
}

public sealed class GitRemoteConnectionResult
{
    public GitServiceInstance? Service { get; init; }
    public required ProcessResult Process { get; init; }
    public bool AuthenticationSucceeded { get; init; }
    public bool PasswordFallbackDetected { get; init; }
    public string Classification { get; init; } = string.Empty;
}
