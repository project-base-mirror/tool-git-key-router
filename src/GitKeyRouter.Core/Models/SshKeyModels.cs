namespace GitKeyRouter.Core.Models;

public sealed class SshKeyGenerationResult
{
    public required GitHubIdentity Identity { get; init; }

    public required ProcessResult Process { get; init; }

    public string PublicKeyText { get; init; } = string.Empty;

    public IReadOnlyList<string> BackupFiles { get; init; } = [];
}

public enum SshKeyFormat
{
    Missing,
    OpenSshPublic,
    Rfc4716Public,
    PemPublic,
    OpenSshPrivate,
    PemPrivate,
    PuttyPrivate,
    Unknown
}

public enum SshPublicKeyExportFormat
{
    OpenSsh,
    Rfc4716,
    Pem
}

public sealed class SshKeyInspectionResult
{
    public SshKeyFormat Format { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public string PublicKeyText { get; init; } = string.Empty;
    public string? Algorithm { get; init; }
    public bool Exists { get; init; }
    public bool IsOpenSsh { get; init; }
    public bool IsPrivateMaterial { get; init; }
    public bool CanConvert { get; init; }
}

public sealed class SshPublicKeyVariant
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required SshKeyInspectionResult Inspection { get; init; }
    public bool IsConfiguredPath { get; init; }
}

public sealed class SshKeyConversionResult
{
    public required SshKeyInspectionResult Source { get; init; }
    public required SshKeyInspectionResult Converted { get; init; }
    public required string DestinationPath { get; init; }
    public ProcessResult? ImportProcess { get; init; }
    public ProcessResult? ExportProcess { get; init; }
    public string? BackupFile { get; init; }
    public bool Changed { get; init; }
}
