using System.Text;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed class GitHubProviderAdapter : IGitProviderAdapter
{
    public GitProviderKind Kind => GitProviderKind.GitHub;

    public IReadOnlyList<GitRemoteUrlPattern> GetSupportedRemotePatterns(GitServiceInstance service)
        => GitRemoteUrlPatternFactory.Create(service);

    public IReadOnlyList<GitUrlRewriteRule> BuildRewriteRules(
        GitServiceInstance service,
        GitIdentity identity,
        RepositoryRoute route)
    {
        var namespacePath = route.NamespacePath.Trim('/');
        var baseUrl = $"{service.SshUser}@{identity.HostAlias}:{namespacePath}/";
        return GetSupportedRemotePatterns(service)
            .Where(pattern => pattern.Kind == GitRemoteUrlPatternKind.Scp
                || pattern.Kind == GitRemoteUrlPatternKind.Web && !pattern.IsInsecureHttp
                || service.EnableExtendedSshUrlRewrites)
            .Select(pattern => new GitUrlRewriteRule(baseUrl, $"{pattern.Prefix}{namespacePath}/"))
            .Distinct()
            .ToList();
    }

    public string BuildSshManagedBlock(
        GitServiceInstance service,
        GitIdentity identity,
        string newline)
    {
        var keyPath = SshConfigService.ConvertWindowsPathToOpenSsh(identity.PrivateKeyPath);
        if (keyPath.Contains(' '))
        {
            keyPath = $"\"{keyPath}\"";
        }

        var builder = new StringBuilder();
        builder.Append(SshConfigService.BeginPrefix).Append(identity.HostAlias).Append(newline);
        builder.Append("Host ").Append(identity.HostAlias).Append(newline);
        builder.Append("    HostName ").Append(service.HostName).Append(newline);
        if (service.SshPort is > 0)
        {
            builder.Append("    Port ").Append(service.SshPort.Value).Append(newline);
        }

        builder.Append("    User ").Append(service.SshUser).Append(newline);
        builder.Append("    IdentityFile ").Append(keyPath).Append(newline);
        builder.Append("    IdentitiesOnly yes").Append(newline);
        builder.Append(SshConfigService.EndPrefix).Append(identity.HostAlias).Append(newline);
        return builder.ToString();
    }

    public ValidationResult ValidateNamespace(string? namespacePath)
        => GitHubOwnerValidator.Validate(namespacePath);

    public bool IsAuthenticationSuccess(ProcessResult result)
        => (result.StandardError + "\n" + result.StandardOutput)
            .Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase);
}
