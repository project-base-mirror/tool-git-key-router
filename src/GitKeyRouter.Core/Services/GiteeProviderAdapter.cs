using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GiteeProviderAdapter : StandardGitProviderAdapter
{
    public GiteeProviderAdapter() : base(GitProviderKind.Gitee, "successfully authenticated", "Welcome")
    {
    }
}
