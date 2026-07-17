using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class IdentityValidator
{
    public static ValidationResult Validate(GitHubIdentity identity, IEnumerable<GitHubIdentity> existing)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(existing);

        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            result.Add("DisplayName is required.");
        }

        var userValidation = GitHubOwnerValidator.Validate(identity.GitHubUsername);
        foreach (var error in userValidation.Errors)
        {
            result.Add($"GitHubUsername: {error}");
        }

        var aliasValidation = HostAliasValidator.Validate(identity.HostAlias);
        foreach (var error in aliasValidation.Errors)
        {
            result.Add(error);
        }

        if (string.IsNullOrWhiteSpace(identity.PrivateKeyPath) || !Path.IsPathFullyQualified(identity.PrivateKeyPath))
        {
            result.Add("PrivateKeyPath must be an absolute path.");
        }

        if (string.IsNullOrWhiteSpace(identity.PublicKeyPath) || !Path.IsPathFullyQualified(identity.PublicKeyPath))
        {
            result.Add("PublicKeyPath must be an absolute path.");
        }

        if (string.Equals(identity.PrivateKeyPath, identity.PublicKeyPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Private and public key paths must be different.");
        }

        if (existing.Any(item => !string.Equals(item.Id, identity.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.HostAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"HostAlias '{identity.HostAlias}' is already used by another identity.");
        }

        return result;
    }
}
