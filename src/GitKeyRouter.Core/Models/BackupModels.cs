namespace GitKeyRouter.Core.Models;

public sealed class BackupManifest
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string BackupDirectory { get; set; } = string.Empty;

    public string? ApplicationVersion { get; set; }

    public bool AppConfigExisted { get; set; }

    public bool SshConfigExisted { get; set; }

    public int GitRewriteCount { get; set; }
}

public sealed class BackupSnapshot
{
    public required BackupManifest Manifest { get; init; }

    public string? AppConfigText { get; init; }

    public string? SshConfigText { get; init; }

    public IReadOnlyList<GitUrlRewriteRule> GitUrlRewrites { get; init; } = [];
}
