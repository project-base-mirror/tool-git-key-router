using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GiteaProviderAdapter : StandardGitProviderAdapter
{
    public GiteaProviderAdapter() : base(GitProviderKind.Gitea, "successfully authenticated", "Hi there")
    {
    }
}
