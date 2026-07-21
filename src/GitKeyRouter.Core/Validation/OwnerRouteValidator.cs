using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.Core.Validation;

public static class OwnerRouteValidator
{
    public static ValidationResult Validate(RepositoryRoute route, AppConfig config, string? originalOwner = null)
        => Validate(
            route,
            config,
            route.ServiceInstanceId,
            originalOwner,
            GitProviderAdapterRegistry.CreateDefault());

    public static ValidationResult Validate(
        RepositoryRoute route,
        AppConfig config,
        string? originalServiceInstanceId,
        string? originalNamespacePath,
        GitProviderAdapterRegistry providers)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(config);

        route.Normalize();
        var result = new ValidationResult();
        var service = config.FindService(route.ServiceInstanceId);
        if (service is null)
        {
            result.Add("The selected Git service does not exist.");
        }
        else
        {
            if (route.Scope == GitRouteScope.Service && service.ProviderKind == GitProviderKind.GitHub)
            {
                result.Add("GitHub service-level routing is not allowed because it would route every account through one SSH identity.");
            }
            else if (route.Scope is GitRouteScope.Owner or GitRouteScope.Repository)
            {
                var namespaceValidation = providers.Get(service.ProviderKind).ValidateNamespace(route.Owner ?? route.NamespacePath);
                foreach (var error in namespaceValidation.Errors)
                {
                    result.Add(error);
                }

                if (route.Scope == GitRouteScope.Repository && string.IsNullOrWhiteSpace(route.Repository))
                {
                    result.Add("Repository is required for a repository-level route.");
                }
            }
        }

        var identity = config.Identities.FirstOrDefault(item =>
            string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
        if (identity is null)
        {
            result.Add("The selected identity does not exist.");
        }
        else if (!string.Equals(identity.ServiceInstanceId, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            result.Add("The selected identity belongs to a different Git service.");
        }

        if (route.Enabled && config.RepositoryRoutes.Any(item => item.Enabled
            && !(string.Equals(item.Id, route.Id, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(item.ServiceInstanceId, originalServiceInstanceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.NamespacePath, originalNamespacePath, StringComparison.OrdinalIgnoreCase)))
            && string.Equals(item.ServiceInstanceId, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase)
            && item.Scope == route.Scope
            && string.Equals(item.RoutePath, route.RoutePath, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"Route '{route.DisplayPath}' already has an enabled {route.Scope} rule for this Git service.");
        }

        return result;
    }
}
