using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Abstractions;

public interface IGitProviderAdapter
{
    GitProviderKind Kind { get; }

    IReadOnlyList<GitRemoteUrlPattern> GetSupportedRemotePatterns(GitServiceInstance service);

    IReadOnlyList<GitUrlRewriteRule> BuildRewriteRules(
        GitServiceInstance service,
        GitIdentity identity,
        RepositoryRoute route);

    string BuildSshManagedBlock(
        GitServiceInstance service,
        GitIdentity identity,
        string newline);

    ValidationResult ValidateNamespace(string? namespacePath);

    bool IsAuthenticationSuccess(ProcessResult result);
}
