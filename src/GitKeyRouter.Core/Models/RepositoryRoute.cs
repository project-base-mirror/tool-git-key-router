using System.Text.Json.Serialization;

namespace GitKeyRouter.Core.Models;

public class RepositoryRoute
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ServiceInstanceId { get; set; } = GitServiceInstance.GitHubComId;

    public GitRouteScope Scope { get; set; } = GitRouteScope.Owner;

    public string? Owner { get; set; }

    public string? Repository { get; set; }

    public string NamespacePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string GitHubOwner
    {
        get => Owner ?? NamespacePath;
        set
        {
            Owner = value;
            NamespacePath = value;
        }
    }

    public string IdentityId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string RoutePath
        => Scope switch
        {
            GitRouteScope.Service => string.Empty,
            GitRouteScope.Owner => NormalizeSegment(Owner ?? NamespacePath),
            GitRouteScope.Repository => BuildRepositoryPath(),
            _ => string.Empty
        };

    [JsonIgnore]
    public string DisplayPath
        => Scope == GitRouteScope.Service ? "<整个服务>" : RoutePath;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        switch (Scope)
        {
            case GitRouteScope.Service:
                Owner = null;
                Repository = null;
                NamespacePath = string.Empty;
                break;
            case GitRouteScope.Owner:
                Owner = NormalizeSegment(Owner ?? NamespacePath);
                Repository = null;
                NamespacePath = Owner;
                break;
            case GitRouteScope.Repository:
                InferRepositoryParts();
                Owner = NormalizeSegment(Owner);
                Repository = NormalizeRepository(Repository);
                NamespacePath = string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repository)
                    ? string.Empty
                    : $"{Owner}/{Repository}";
                break;
        }
    }

    private string BuildRepositoryPath()
    {
        var owner = NormalizeSegment(Owner);
        var repository = NormalizeRepository(Repository);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
        {
            var legacy = NormalizeSegment(NamespacePath);
            var separator = legacy.LastIndexOf('/');
            if (separator <= 0 || separator == legacy.Length - 1)
            {
                return legacy;
            }

            owner = legacy[..separator];
            repository = NormalizeRepository(legacy[(separator + 1)..]);
        }

        return $"{owner}/{repository}";
    }

    private void InferRepositoryParts()
    {
        if (!string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository))
        {
            return;
        }

        var legacy = NormalizeSegment(NamespacePath);
        var separator = legacy.LastIndexOf('/');
        if (separator > 0 && separator < legacy.Length - 1)
        {
            Owner ??= legacy[..separator];
            Repository ??= legacy[(separator + 1)..];
        }
    }

    private static string NormalizeSegment(string? value)
        => (value ?? string.Empty).Trim().Trim('/');

    private static string NormalizeRepository(string? value)
    {
        var repository = NormalizeSegment(value);
        return repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repository
            : repository.Length == 0 ? string.Empty : repository + ".git";
    }
}
