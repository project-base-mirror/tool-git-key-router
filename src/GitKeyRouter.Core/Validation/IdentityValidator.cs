using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class IdentityValidator
{
    public static ValidationResult Validate(GitIdentity identity, IEnumerable<GitIdentity> existing)
        => ValidateCore(identity, existing);

    public static ValidationResult Validate(
        GitIdentity identity,
        AppConfig config,
        IEnumerable<string>? unmanagedHostAliases = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = ValidateCore(identity, config.Identities);
        if (string.IsNullOrWhiteSpace(identity.ServiceInstanceId)
            || config.FindService(identity.ServiceInstanceId) is null)
        {
            result.Add("ServiceInstanceId must reference an existing Git service.");
        }

        if (config.Identities.Any(item => !string.Equals(item.Id, identity.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.ServiceInstanceId, identity.ServiceInstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.AccountName, identity.AccountName, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"AccountName '{identity.AccountName}' is already configured for this Git service.");
        }

        var normalizedAlias = NormalizeHost(identity.HostAlias);
        var conflictingService = config.GitServices.FirstOrDefault(service =>
            string.Equals(NormalizeHost(service.HostName), normalizedAlias, StringComparison.OrdinalIgnoreCase));
        if (conflictingService is not null)
        {
            result.Add(
                $"HostAlias '{identity.HostAlias}' conflicts with the real host name of Git service '{conflictingService.DisplayName}'.");
        }

        if (unmanagedHostAliases?.Any(alias =>
                string.Equals(NormalizeHost(alias), normalizedAlias, StringComparison.OrdinalIgnoreCase)) == true)
        {
            result.Add($"HostAlias '{identity.HostAlias}' already exists in an unmanaged SSH Host declaration.");
        }

        return result;
    }

    private static string NormalizeHost(string? value)
        => (value ?? string.Empty).Trim().TrimEnd('.');

    private static ValidationResult ValidateCore(GitIdentity identity, IEnumerable<GitIdentity> existing)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(existing);

        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(identity.DisplayName))
        {
            result.Add("DisplayName is required.");
        }

        if (string.IsNullOrWhiteSpace(identity.AccountName))
        {
            result.Add("AccountName is required.");
        }
        else if (identity.AccountName.Length > 200
                 || identity.AccountName.Any(char.IsControl))
        {
            result.Add("AccountName is invalid or too long.");
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
