namespace GitKeyRouter.Core.Models;

public sealed record GitUrlRewriteRule(string BaseUrl, string InsteadOfUrl, string? Origin = null)
{
    public string ConfigKey => $"url.{BaseUrl}.insteadOf";
}

public enum GitRewriteStatus
{
    Correct,
    Missing,
    Duplicate,
    Conflict,
    Extra,
    Disabled
}

public sealed class GitRewriteComparison
{
    public string? ServiceInstanceId { get; init; }

    public string? NamespacePath { get; init; }

    public string? GitHubOwner { get; init; }

    public string? IdentityId { get; init; }

    public string? IdentityDisplayName { get; init; }

    public required string ExpectedBaseUrl { get; init; }

    public required string InsteadOfUrl { get; init; }

    public required GitRewriteStatus Status { get; init; }

    public int ActualMatchCount { get; init; }

    public IReadOnlyList<GitUrlRewriteRule> ActualRules { get; init; } = [];
}

public sealed class GitRewritePlan
{
    public List<GitUrlRewriteRule> Adds { get; } = [];

    public List<GitUrlRewriteRule> Removes { get; } = [];

    public bool HasChanges => Adds.Count > 0 || Removes.Count > 0;
}
