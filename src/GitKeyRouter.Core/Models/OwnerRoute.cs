namespace GitKeyRouter.Core.Models;

public sealed class OwnerRoute
{
    public string GitHubOwner { get; set; } = string.Empty;

    public string IdentityId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
