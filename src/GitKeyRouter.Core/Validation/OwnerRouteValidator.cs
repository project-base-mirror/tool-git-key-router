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

        var result = new ValidationResult();
        var service = config.FindService(route.ServiceInstanceId);
        if (service is null)
        {
            result.Add("The selected Git service does not exist.");
        }
        else
        {
            var namespaceValidation = providers.Get(service.ProviderKind).ValidateNamespace(route.NamespacePath);
            foreach (var error in namespaceValidation.Errors)
            {
                result.Add(error);
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
            && !(string.Equals(item.ServiceInstanceId, originalServiceInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.NamespacePath, originalNamespacePath, StringComparison.OrdinalIgnoreCase))
            && string.Equals(item.ServiceInstanceId, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.NamespacePath, route.NamespacePath, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add($"Namespace '{route.NamespacePath}' already has an enabled route for this Git service.");
        }

        return result;
    }
}
