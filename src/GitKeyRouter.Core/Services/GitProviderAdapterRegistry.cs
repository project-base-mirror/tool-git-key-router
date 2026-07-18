using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GitProviderAdapterRegistry
{
    private readonly IReadOnlyDictionary<GitProviderKind, IGitProviderAdapter> _adapters;

    public GitProviderAdapterRegistry(IEnumerable<IGitProviderAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(item => item.Kind);
    }

    public static GitProviderAdapterRegistry CreateDefault()
        => new([new GitHubProviderAdapter()]);

    public IGitProviderAdapter Get(GitProviderKind kind)
        => _adapters.TryGetValue(kind, out var adapter)
            ? adapter
            : throw new NotSupportedException($"Git provider '{kind}' is not registered.");
}
