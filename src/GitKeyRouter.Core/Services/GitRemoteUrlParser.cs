using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GitRemoteUrlParser
{
    private readonly GitProviderAdapterRegistry _providers;

    public GitRemoteUrlParser(GitProviderAdapterRegistry? providers = null)
    {
        _providers = providers ?? GitProviderAdapterRegistry.CreateDefault();
    }

    public GitRemoteUrlMatch? Parse(string? remoteUrl, IEnumerable<GitServiceInstance> services)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var original = remoteUrl.Trim();
        foreach (var service in services)
        {
            foreach (var pattern in _providers.Get(service.ProviderKind)
                         .GetSupportedRemotePatterns(service)
                         .OrderByDescending(item => item.Prefix.Length))
            {
                if (!original.StartsWith(pattern.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = original[pattern.Prefix.Length..]
                    .Split(['?', '#'], 2)[0]
                    .Trim('/');
                var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Length < 2)
                {
                    continue;
                }

                var repositoryName = segments[^1];
                if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    repositoryName = repositoryName[..^4];
                }

                if (string.IsNullOrWhiteSpace(repositoryName))
                {
                    continue;
                }

                return new GitRemoteUrlMatch
                {
                    OriginalUrl = original,
                    ServiceInstanceId = service.Id,
                    ServiceDisplayName = service.DisplayName,
                    PatternKind = pattern.Kind,
                    MatchedPrefix = pattern.Prefix,
                    NamespacePath = string.Join('/', segments[..^1]),
                    RepositoryName = repositoryName,
                    UsesInsecureHttp = pattern.IsInsecureHttp,
                    IncludesPort = pattern.IncludesPort,
                    HasWebSubPath = pattern.HasWebSubPath
                };
            }
        }

        return null;
    }
}
