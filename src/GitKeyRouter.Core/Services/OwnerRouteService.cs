using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed class OwnerRouteService
{
    private readonly IAppConfigStore _configStore;
    private readonly IBackupService _backupService;
    private readonly GitProviderAdapterRegistry _providers;

    public OwnerRouteService(
        IAppConfigStore configStore,
        IBackupService backupService,
        GitProviderAdapterRegistry? providers = null)
    {
        _configStore = configStore;
        _backupService = backupService;
        _providers = providers ?? GitProviderAdapterRegistry.CreateDefault();
    }

    public async Task<IReadOnlyList<RepositoryRoute>> ListAsync(CancellationToken cancellationToken = default)
        => (await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false)).OwnerRoutes
            .OrderBy(item => item.GitHubOwner, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<OperationResult<RepositoryRoute>> SaveAsync(
        RepositoryRoute route,
        string? originalOwner = null,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var validation = OwnerRouteValidator.Validate(route, config, originalOwner);
        if (!validation.IsValid)
        {
            return OperationResult<RepositoryRoute>.Fail("Owner route validation failed.", validation.Errors.ToArray());
        }

        await _backupService.CreateSnapshotAsync($"Save owner route: {route.GitHubOwner}", cancellationToken).ConfigureAwait(false);
        var existing = config.OwnerRoutes.FirstOrDefault(item => string.Equals(item.GitHubOwner, originalOwner ?? route.GitHubOwner, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            config.OwnerRoutes.Add(route);
        }
        else
        {
            existing.GitHubOwner = route.GitHubOwner;
            existing.IdentityId = route.IdentityId;
            existing.Enabled = route.Enabled;
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult<RepositoryRoute>.Ok(route, "Owner route saved.");
    }

    public async Task<OperationResult> DeleteAsync(string owner, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var route = config.OwnerRoutes.FirstOrDefault(item => string.Equals(item.GitHubOwner, owner, StringComparison.OrdinalIgnoreCase));
        if (route is null)
        {
            return OperationResult.Fail("Owner route was not found.");
        }

        await _backupService.CreateSnapshotAsync($"Delete owner route: {owner}", cancellationToken).ConfigureAwait(false);
        config.OwnerRoutes.Remove(route);
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Owner route deleted from application configuration.");
    }

    public static IReadOnlyList<GitUrlRewriteRule> BuildExpectedRules(
        AppConfig config,
        GitProviderAdapterRegistry? providers = null)
    {
        providers ??= GitProviderAdapterRegistry.CreateDefault();
        var identities = config.Identities.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var rules = new List<GitUrlRewriteRule>();
        foreach (var route in config.OwnerRoutes.Where(item => item.Enabled))
        {
            if (!identities.TryGetValue(route.IdentityId, out var identity))
            {
                continue;
            }

            var service = config.FindService(route.ServiceInstanceId);
            if (service is null
                || !string.Equals(identity.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rules.AddRange(providers.Get(service.ProviderKind).BuildRewriteRules(service, identity, route));
        }

        return rules;
    }
}
