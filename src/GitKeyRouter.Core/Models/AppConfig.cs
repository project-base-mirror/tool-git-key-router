using System.Text.Json.Serialization;

namespace GitKeyRouter.Core.Models;

public sealed class AppConfig
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<GitServiceInstance> GitServices { get; set; } = [GitServiceInstance.CreateGitHubCom()];

    public List<GitIdentity> Identities { get; set; } = [];

    public List<RepositoryRoute> RepositoryRoutes { get; set; } = [];

    public List<GitProfile> GitProfiles { get; set; } = [];

    public List<GitProfileRule> GitProfileRules { get; set; } = [];

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
        GitProfiles ??= [];
        GitProfileRules ??= [];

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

        foreach (var service in GitServices)
        {
            service.HostName = service.HostName.Trim();
            service.WebBaseUrl = service.WebBaseUrl.TrimEnd('/');
            service.SshPort ??= 22;
        }

        foreach (var route in RepositoryRoutes)
        {
            if (string.IsNullOrWhiteSpace(route.ServiceInstanceId))
            {
                route.ServiceInstanceId = GitServiceInstance.GitHubComId;
            }

            route.Normalize();
        }

        SynchronizeDefaultServiceRoutes();
    }

    public void SynchronizeDefaultServiceRoutes()
    {
        foreach (var service in GitServices)
        {
            var managedRouteId = $"service-default:{service.Id}";
            var managedRoute = RepositoryRoutes.FirstOrDefault(item =>
                string.Equals(item.Id, managedRouteId, StringComparison.OrdinalIgnoreCase));
            var defaultIdentity = string.IsNullOrWhiteSpace(service.DefaultIdentityId)
                ? null
                : Identities.FirstOrDefault(item =>
                    string.Equals(item.Id, service.DefaultIdentityId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase));

            if (service.ProviderKind == GitProviderKind.GitHub || defaultIdentity is null)
            {
                if (managedRoute is not null)
                {
                    RepositoryRoutes.Remove(managedRoute);
                }

                continue;
            }

            var serviceRoute = managedRoute ?? RepositoryRoutes.FirstOrDefault(item =>
                item.Scope == GitRouteScope.Service
                && string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase));
            if (serviceRoute is null)
            {
                serviceRoute = new RepositoryRoute { Id = managedRouteId };
                RepositoryRoutes.Add(serviceRoute);
            }

            serviceRoute.ServiceInstanceId = service.Id;
            serviceRoute.IdentityId = defaultIdentity.Id;
            serviceRoute.Scope = GitRouteScope.Service;
            serviceRoute.Enabled = true;
            serviceRoute.Normalize();
        }
    }
}
