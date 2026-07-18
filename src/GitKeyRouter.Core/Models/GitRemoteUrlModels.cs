namespace GitKeyRouter.Core.Models;

public enum GitRemoteUrlPatternKind
{
    Web,
    Scp,
    Ssh,
    GitSsh
}

public sealed record GitRemoteUrlPattern(
    GitRemoteUrlPatternKind Kind,
    string Prefix,
    bool IsInsecureHttp = false,
    bool IncludesPort = false,
    bool HasWebSubPath = false);

public sealed class GitRemoteUrlMatch
{
    public required string OriginalUrl { get; init; }

    public required string ServiceInstanceId { get; init; }

    public required string ServiceDisplayName { get; init; }

    public required GitRemoteUrlPatternKind PatternKind { get; init; }

    public required string MatchedPrefix { get; init; }

    public required string NamespacePath { get; init; }

    public required string RepositoryName { get; init; }

    public bool UsesInsecureHttp { get; init; }

    public bool IncludesPort { get; init; }

    public bool HasWebSubPath { get; init; }
}
