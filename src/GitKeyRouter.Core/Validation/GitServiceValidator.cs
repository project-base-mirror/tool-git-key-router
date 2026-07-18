using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class GitServiceValidator
{
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

        var endpoint = $"{service.HostName.Trim()}:{service.SshPort ?? 22}";
        if (existing.Any(item => !string.Equals(item.Id, service.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals($"{item.HostName.Trim()}:{item.SshPort ?? 22}", endpoint, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"SSH endpoint '{endpoint}' is already configured.");
        }

        return result;
    }
}
