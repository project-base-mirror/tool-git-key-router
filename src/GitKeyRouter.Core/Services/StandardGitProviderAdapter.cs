using System.Text;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public abstract class StandardGitProviderAdapter : IGitProviderAdapter
{
    private readonly string[] _successPhrases;

    protected StandardGitProviderAdapter(GitProviderKind kind, params string[] successPhrases)
    {
        Kind = kind;
        _successPhrases = successPhrases;
    }

    public GitProviderKind Kind { get; }

    public virtual IReadOnlyList<GitUrlRewriteRule> BuildRewriteRules(
        GitServiceInstance service,
        GitIdentity identity,
        RepositoryRoute route)
    {
        var namespacePath = route.NamespacePath.Trim('/');
        var baseUrl = $"{service.SshUser}@{identity.HostAlias}:{namespacePath}/";
        return
        [
            new GitUrlRewriteRule(baseUrl, $"{service.WebBaseUrl.TrimEnd('/')}/{namespacePath}/"),
            new GitUrlRewriteRule(baseUrl, $"{service.SshUser}@{service.HostName}:{namespacePath}/")
        ];
    }

    public virtual string BuildSshManagedBlock(
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
        if (service.SshPort is > 0 and not 22)
        {
            builder.Append("    Port ").Append(service.SshPort.Value).Append(newline);
        }

        builder.Append("    User ").Append(service.SshUser).Append(newline);
        builder.Append("    IdentityFile ").Append(keyPath).Append(newline);
        builder.Append("    IdentitiesOnly yes").Append(newline);
        builder.Append(SshConfigService.EndPrefix).Append(identity.HostAlias).Append(newline);
        return builder.ToString();
    }

    public virtual ValidationResult ValidateNamespace(string? namespacePath)
        => RepositoryNamespaceValidator.Validate(namespacePath);

    public virtual bool IsAuthenticationSuccess(ProcessResult result)
    {
        var output = result.StandardOutput + "\n" + result.StandardError;
        return result.Succeeded || _successPhrases.Any(phrase => output.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
}
