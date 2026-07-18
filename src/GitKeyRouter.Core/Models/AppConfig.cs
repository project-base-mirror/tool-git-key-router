using System.Text.Json.Serialization;

namespace GitKeyRouter.Core.Models;

public sealed class AppConfig
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<GitServiceInstance> GitServices { get; set; } = [GitServiceInstance.CreateGitHubCom()];

    public List<GitIdentity> Identities { get; set; } = [];

    public List<RepositoryRoute> RepositoryRoutes { get; set; } = [];

    [JsonIgnore]
    public List<RepositoryRoute> OwnerRoutes
    {
        get => RepositoryRoutes;
        set => RepositoryRoutes = value ?? [];
    }

    public GitServiceInstance? FindService(string serviceInstanceId)
        => GitServices.FirstOrDefault(item => string.Equals(item.Id, serviceInstanceId, StringComparison.OrdinalIgnoreCase));

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        GitServices ??= [];
        Identities ??= [];
        RepositoryRoutes ??= [];

        if (!GitServices.Any(item => string.Equals(item.Id, GitServiceInstance.GitHubComId, StringComparison.OrdinalIgnoreCase)))
        {
            GitServices.Insert(0, GitServiceInstance.CreateGitHubCom());
        }

        foreach (var identity in Identities)
        {
            if (string.IsNullOrWhiteSpace(identity.ServiceInstanceId))
            {
                identity.ServiceInstanceId = GitServiceInstance.GitHubComId;
            }
        }

        foreach (var route in RepositoryRoutes)
        {
            if (string.IsNullOrWhiteSpace(route.ServiceInstanceId))
            {
                route.ServiceInstanceId = GitServiceInstance.GitHubComId;
            }
        }
    }
}
