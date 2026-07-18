namespace GitKeyRouter.Core.Models;

public enum RequiredToolKind
{
    Git,
    OpenSshClient
}

public sealed class RequiredToolInstallItem
{
    public required RequiredToolKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string Reason { get; init; }
    public required string InstallMethod { get; init; }
    public required string ManualInstallUri { get; init; }
    public bool CanInstallAutomatically { get; init; }
}

public sealed class RequiredToolInstallPlan
{
    public required ToolchainInfo Toolchain { get; init; }
    public IReadOnlyList<RequiredToolInstallItem> MissingTools { get; init; } = [];

    public bool HasMissingTools => MissingTools.Count > 0;

    public bool CanInstallAllAutomatically
        => HasMissingTools && MissingTools.All(item => item.CanInstallAutomatically);
}

public sealed class RequiredToolInstallStep
{
    public required RequiredToolKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public ProcessResult? ProcessResult { get; init; }
}

public sealed class RequiredToolInstallResult
{
    public required ToolchainInfo Before { get; init; }
    public required ToolchainInfo After { get; init; }
    public IReadOnlyList<RequiredToolInstallStep> Steps { get; init; } = [];

    public bool AllRequiredToolsAvailable
        => After.Git.Exists && After.Ssh.Exists && After.SshKeygen.Exists;
}
