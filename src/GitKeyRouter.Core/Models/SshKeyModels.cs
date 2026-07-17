namespace GitKeyRouter.Core.Models;

public sealed class SshKeyGenerationResult
{
    public required GitHubIdentity Identity { get; init; }

    public required ProcessResult Process { get; init; }

    public string PublicKeyText { get; init; } = string.Empty;

    public IReadOnlyList<string> BackupFiles { get; init; } = [];
}
