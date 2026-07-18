namespace GitKeyRouter.Core.Models;

public sealed class SshKeyFileMove
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
}

public sealed class SshKeyPathReplacement
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
}

public sealed class SshKeyRenamePlan
{
    public required string IdentityId { get; init; }
    public required string IdentityDisplayName { get; init; }
    public required string NewBaseName { get; init; }
    public required string OriginalPrivateKeyPath { get; init; }
    public required string OriginalPublicKeyPath { get; init; }
    public required string NewPrivateKeyPath { get; init; }
    public required string NewPublicKeyPath { get; init; }
    public IReadOnlyList<SshKeyFileMove> FileMoves { get; init; } = [];
    public IReadOnlyList<SshKeyPathReplacement> PathReplacements { get; init; } = [];
    public IReadOnlyList<string> AffectedIdentityIds { get; init; } = [];
    public IReadOnlyList<string> AffectedIdentityNames { get; init; } = [];
    public string SshConfigDiff { get; init; } = string.Empty;
}

public sealed class SshKeyRenameResult
{
    public required SshKeyRenamePlan Plan { get; init; }
    public IReadOnlyList<string> BackupFiles { get; init; } = [];
}
