using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GenericGitProviderAdapter : StandardGitProviderAdapter
{
    public GenericGitProviderAdapter() : base(GitProviderKind.Generic, "successfully authenticated", "Welcome")
    {
    }
}
