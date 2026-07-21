namespace GitKeyRouter.Core.Models;

public sealed class ChangePreview
{
    public required string Description { get; init; }

    public required string OriginalText { get; init; }

    public required string UpdatedText { get; init; }

    public required string DiffText { get; init; }

    public bool HasChanges => !string.Equals(OriginalText, UpdatedText, StringComparison.Ordinal);
}

public sealed class UrlRewritePreview
{
    public required string OriginalUrl { get; init; }

    public string? MatchedPrefix { get; init; }

    public string? MatchedBaseUrl { get; init; }

    public string? ActualMatchedPrefix { get; init; }

    public string? ActualMatchedBaseUrl { get; init; }

    public string? ActualRewrittenUrl { get; init; }

    public string? ExpectedMatchedPrefix { get; init; }

    public string? ExpectedMatchedBaseUrl { get; init; }

    public string? ExpectedRewrittenUrl { get; init; }

    public UrlRewriteExpectationStatus ExpectationStatus { get; init; }

    public required string RewrittenUrl { get; init; }

    public bool WasRewritten => MatchedPrefix is not null;
}

public enum UrlRewriteExpectationStatus
{
    None,
    Applied,
    Missing,
    Conflict
}

public sealed class SshTestResult
{
    public required string HostAlias { get; init; }

    public required ProcessResult Process { get; init; }

    public bool Authenticated { get; init; }

    public string Classification { get; init; } = string.Empty;
}
