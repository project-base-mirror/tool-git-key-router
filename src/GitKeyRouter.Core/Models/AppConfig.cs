namespace GitKeyRouter.Core.Models;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public List<GitHubIdentity> Identities { get; set; } = [];

    public List<OwnerRoute> OwnerRoutes { get; set; } = [];
}
