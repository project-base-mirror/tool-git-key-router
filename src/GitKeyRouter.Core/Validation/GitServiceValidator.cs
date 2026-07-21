using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class GitServiceValidator
{
    public static ValidationResult Validate(GitServiceInstance service, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var result = Validate(service, config.GitServices);
        var webBaseUrl = NormalizeWebBaseUrl(service.WebBaseUrl);
        if (config.GitServices.Any(item => !string.Equals(item.Id, service.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeWebBaseUrl(item.WebBaseUrl), webBaseUrl, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"Web base URL '{service.WebBaseUrl}' is already configured by another Git service.");
        }

        if (!string.IsNullOrWhiteSpace(service.DefaultIdentityId))
        {
            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, service.DefaultIdentityId, StringComparison.OrdinalIgnoreCase));
            if (identity is null)
            {
                result.Add("DefaultIdentityId must reference an existing identity.");
            }
            else if (!string.Equals(identity.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Add("DefaultIdentityId must belong to the same Git service.");
            }

            if (service.ProviderKind == GitProviderKind.GitHub)
            {
                result.Add("GitHub cannot use a service-level default identity; configure Owner routes instead.");
            }
        }

        return result;
    }

    public static ValidationResult Validate(GitServiceInstance service, IEnumerable<GitServiceInstance> existing)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(service.DisplayName))
        {
            result.Add("Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(service.Id))
        {
            result.Add("Service ID is required.");
        }

        if (string.IsNullOrWhiteSpace(service.HostName)
            || Uri.CheckHostName(service.HostName.Trim()) == UriHostNameType.Unknown)
        {
            result.Add("Host name is invalid.");
        }

        if (string.IsNullOrWhiteSpace(service.SshUser))
        {
            result.Add("SSH user is required.");
        }

        if (service.SshPort is <= 0 or > 65535)
        {
            result.Add("SSH port must be between 1 and 65535.");
        }

        if (!Uri.TryCreate(service.WebBaseUrl, UriKind.Absolute, out var webUri)
            || webUri.Scheme is not ("http" or "https"))
        {
            result.Add("Web base URL must be an absolute HTTP or HTTPS URL.");
        }

        var endpoint = service.HostName.Trim();
        if (existing.Any(item => !string.Equals(item.Id, service.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.HostName.Trim(), endpoint, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"SSH host source '{endpoint}' is already configured by another Git service.");
        }

        return result;
    }

    private static string NormalizeWebBaseUrl(string? value)
        => (value ?? string.Empty).Trim().TrimEnd('/');
}
