using System.Text.Json.Serialization;

namespace GitKeyRouter.Core.Models;

public class RepositoryRoute
{
    public string ServiceInstanceId { get; set; } = GitServiceInstance.GitHubComId;

    public string NamespacePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string GitHubOwner
    {
        get => NamespacePath;
        set => NamespacePath = value;
    }

    public string IdentityId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
