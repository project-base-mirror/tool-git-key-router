namespace GitKeyRouter.Core.Models;

public sealed class BackupManifest
{
    public int SchemaVersion { get; set; } = 2;

    public DateTimeOffset CreatedAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string BackupDirectory { get; set; } = string.Empty;

    public string? ApplicationVersion { get; set; }

    public bool AppConfigExisted { get; set; }

    public int? AppConfigSchemaVersion { get; set; }

    public bool SshConfigExisted { get; set; }

    public int GitRewriteCount { get; set; }

    public string? GitRewriteCaptureError { get; set; }

    public Dictionary<string, BackupFileIntegrity> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BackupFileIntegrity
{
    public long Length { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class BackupSnapshot
{
    public required BackupManifest Manifest { get; init; }

    public string? AppConfigText { get; init; }

    public string? SshConfigText { get; init; }

    public IReadOnlyList<GitUrlRewriteRule> GitUrlRewrites { get; init; } = [];
}
