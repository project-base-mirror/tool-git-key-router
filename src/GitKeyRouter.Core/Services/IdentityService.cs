using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed class IdentityService
{
    private readonly IAppConfigStore _configStore;
    private readonly IBackupService _backupService;
    private readonly IClock _clock;

    public IdentityService(IAppConfigStore configStore, IBackupService backupService, IClock clock)
    {
        _configStore = configStore;
        _backupService = backupService;
        _clock = clock;
    }

    public async Task<IReadOnlyList<GitHubIdentity>> ListAsync(CancellationToken cancellationToken = default)
        => (await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false)).Identities
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<OperationResult<GitHubIdentity>> SaveAsync(GitHubIdentity identity, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var validation = IdentityValidator.Validate(identity, config.Identities);
        if (!validation.IsValid)
        {
            return OperationResult<GitHubIdentity>.Fail("Identity validation failed.", validation.Errors.ToArray());
        }

        var existingIndex = config.Identities.FindIndex(item => string.Equals(item.Id, identity.Id, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            identity.Id = Guid.NewGuid().ToString("N");
        }

        if (identity.CreatedAt == default)
        {
            identity.CreatedAt = _clock.UtcNow;
        }

        await _backupService.CreateSnapshotAsync($"Save identity: {identity.DisplayName}", cancellationToken).ConfigureAwait(false);
        if (existingIndex >= 0)
        {
            config.Identities[existingIndex] = identity;
        }
        else
        {
            config.Identities.Add(identity);
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult<GitHubIdentity>.Ok(identity, "Identity saved.");
    }

    public async Task<OperationResult> DeleteAsync(string identityId, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var identity = config.Identities.FirstOrDefault(item => string.Equals(item.Id, identityId, StringComparison.OrdinalIgnoreCase));
        if (identity is null)
        {
            return OperationResult.Fail("Identity was not found.");
        }

        await _backupService.CreateSnapshotAsync($"Delete identity record: {identity.DisplayName}", cancellationToken).ConfigureAwait(false);
        config.Identities.Remove(identity);
        foreach (var route in config.OwnerRoutes.Where(item => string.Equals(item.IdentityId, identityId, StringComparison.OrdinalIgnoreCase)))
        {
            route.Enabled = false;
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Identity record deleted. Key files were not deleted; related routes were disabled.");
    }
}
