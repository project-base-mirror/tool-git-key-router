using System.Text.Json.Serialization;

namespace GitKeyRouter.Core.Models;

public class GitIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ServiceInstanceId { get; set; } = GitServiceInstance.GitHubComId;

    public string DisplayName { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    [JsonIgnore]
    public string GitHubUsername
    {
        get => AccountName;
        set => AccountName = value;
    }

    public string HostAlias { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string PublicKeyPath { get; set; } = string.Empty;

    public string EmailOrComment { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
