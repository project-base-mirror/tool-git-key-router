namespace GitKeyRouter.Core.Models;

public sealed class GitHubIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string GitHubUsername { get; set; } = string.Empty;

    public string HostAlias { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string PublicKeyPath { get; set; } = string.Empty;

    public string EmailOrComment { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
