using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class OwnerRouteValidator
{
    public static ValidationResult Validate(RepositoryRoute route, AppConfig config, string? originalOwner = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(config);

        var result = GitHubOwnerValidator.Validate(route.GitHubOwner);
        if (!config.Identities.Any(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("The selected identity does not exist.");
        }

        if (route.Enabled && config.OwnerRoutes.Any(item => item.Enabled
            && !string.Equals(item.GitHubOwner, originalOwner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.GitHubOwner, route.GitHubOwner, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"Owner '{route.GitHubOwner}' already has an enabled route.");
        }

        return result;
    }
}
