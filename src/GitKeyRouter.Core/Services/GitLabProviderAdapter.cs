using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GitLabProviderAdapter : StandardGitProviderAdapter
{
    public GitLabProviderAdapter() : base(GitProviderKind.GitLab, "Welcome to GitLab")
    {
    }
}
