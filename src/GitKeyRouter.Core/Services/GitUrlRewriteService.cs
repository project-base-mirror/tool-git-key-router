using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GitUrlRewriteService
{
    private readonly IAppConfigStore _configStore;
    private readonly IGitUrlRewriteStore _store;
    private readonly IBackupService _backupService;
    private readonly GitProviderAdapterRegistry _providers;
    private readonly GitRemoteUrlParser _remoteUrlParser;

    public GitUrlRewriteService(
        IAppConfigStore configStore,
        IGitUrlRewriteStore store,
        IBackupService backupService,
        GitProviderAdapterRegistry? providers = null)
    {
        _configStore = configStore;
        _store = store;
        _backupService = backupService;
        _providers = providers ?? GitProviderAdapterRegistry.CreateDefault();
        _remoteUrlParser = new GitRemoteUrlParser(_providers);
    }

    public async Task<IReadOnlyList<GitRewriteComparison>> CompareAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expectedEntries = BuildExpectedEntries(config);
        var expected = expectedEntries.Select(item => item.Rule).ToList();
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var legacyRules = FindLegacyAccountOwnerRules(config, actual);
        var comparisons = new List<GitRewriteComparison>();

        foreach (var entry in expectedEntries)
        {
            var expectedRule = entry.Rule;
            var exact = actual.Where(item => RuleEquals(item, expectedRule)).ToList();
            var samePrefix = actual.Where(item => string.Equals(item.InsteadOfUrl, expectedRule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase)).ToList();
            var status = IsLegacyAccountOwnerEntry(entry)
                ? GitRewriteStatus.LegacyAccountOwner
                : exact.Count switch
                {
                    0 when samePrefix.Count > 0 => GitRewriteStatus.Conflict,
                    0 => GitRewriteStatus.Missing,
                    1 when samePrefix.Count == 1 => GitRewriteStatus.Correct,
                    _ => GitRewriteStatus.Duplicate
                };

            comparisons.Add(new GitRewriteComparison
            {
                ServiceInstanceId = entry.Route.ServiceInstanceId,
                NamespacePath = entry.Route.NamespacePath,
                GitHubOwner = entry.Service.ProviderKind == GitProviderKind.GitHub ? entry.Route.NamespacePath : null,
                IdentityId = entry.Identity.Id,
                IdentityDisplayName = entry.Identity.DisplayName,
                ExpectedBaseUrl = expectedRule.BaseUrl,
                InsteadOfUrl = expectedRule.InsteadOfUrl,
                Status = status,
                ActualMatchCount = exact.Count,
                ActualRules = samePrefix
            });
        }

        foreach (var extra in actual.Where(item => !expected.Any(expectedRule => RuleEquals(item, expectedRule))))
        {
            comparisons.Add(new GitRewriteComparison
            {
                ExpectedBaseUrl = extra.BaseUrl,
                InsteadOfUrl = extra.InsteadOfUrl,
                Status = legacyRules.Any(item => RuleEquals(item, extra))
                    ? GitRewriteStatus.LegacyAccountOwner
                    : GitRewriteStatus.Extra,
                ActualMatchCount = 1,
                ActualRules = [extra]
            });
        }

        return comparisons;
    }

    public Task<IReadOnlyList<GitUrlRewriteRule>> GetActualRulesAsync(CancellationToken cancellationToken = default)
        => _store.GetAllAsync(cancellationToken);

    public Task<OperationResult<IReadOnlyList<string>>> GetGlobalConfigOriginsAsync(CancellationToken cancellationToken = default)
        => _store.GetGlobalConfigOriginsAsync(cancellationToken);

    public async Task<GitRewritePlan> BuildApplyMissingPlanAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildCurrentExpectedRules(config);
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        foreach (var rule in expected)
        {
            var samePrefix = actual.Any(item => string.Equals(item.InsteadOfUrl, rule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase));
            if (!samePrefix)
            {
                plan.Adds.Add(rule);
            }
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildCleanupDuplicatesPlanAsync(CancellationToken cancellationToken = default)
    {
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        foreach (var group in actual.GroupBy(rule => $"{rule.BaseUrl}\n{rule.InsteadOfUrl}", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            var rule = group.First();
            plan.Removes.Add(rule);
            plan.Adds.Add(rule);
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildDeleteRulePlanAsync(
        string baseUrl,
        string insteadOfUrl,
        CancellationToken cancellationToken = default)
    {
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        var rule = actual.FirstOrDefault(item => string.Equals(item.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.InsteadOfUrl, insteadOfUrl, StringComparison.OrdinalIgnoreCase));
        if (rule is not null)
        {
            plan.Removes.Add(rule);
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildDeleteOwnerPlanAsync(string owner, CancellationToken cancellationToken = default)
        => await BuildDeleteRoutePlanAsync(
            GitServiceInstance.GitHubComId,
            owner,
            cancellationToken).ConfigureAwait(false);

    public async Task<GitRewritePlan> BuildDeleteRoutePlanAsync(
        string serviceInstanceId,
        string namespacePath,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildExpectedEntries(config)
            .Where(item => string.Equals(item.Route.ServiceInstanceId, serviceInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Route.NamespacePath, namespacePath, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Rule)
            .ToList();
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        foreach (var rule in expected)
        {
            if (actual.Any(item => RuleEquals(item, rule))
                && !plan.Removes.Any(item => RuleEquals(item, rule)))
            {
                plan.Removes.Add(rule);
            }
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildDeleteRouteByIdPlanAsync(
        string routeId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildExpectedEntries(config)
            .Where(item => string.Equals(item.Route.Id, routeId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Rule)
            .ToList();
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        foreach (var rule in expected)
        {
            if (actual.Any(item => RuleEquals(item, rule))
                && !plan.Removes.Any(item => RuleEquals(item, rule)))
            {
                plan.Removes.Add(rule);
            }
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildRegeneratePlanAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildCurrentExpectedRules(config);
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();
        foreach (var expectedRule in expected)
        {
            foreach (var currentRule in actual.Where(item => string.Equals(item.InsteadOfUrl, expectedRule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase)))
            {
                if (!plan.Removes.Any(item => RuleEquals(item, currentRule)))
                {
                    plan.Removes.Add(currentRule);
                }
            }

            plan.Adds.Add(expectedRule);
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildReconcilePlanAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildCurrentExpectedRules(config);
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new GitRewritePlan();

        foreach (var expectedRule in expected)
        {
            var samePrefix = actual.Where(item => string.Equals(item.InsteadOfUrl, expectedRule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase)).ToList();
            var exactCount = samePrefix.Count(item => RuleEquals(item, expectedRule));
            if (exactCount == 1 && samePrefix.Count == 1)
            {
                continue;
            }

            foreach (var rule in samePrefix)
            {
                if (!plan.Removes.Any(item => RuleEquals(item, rule)))
                {
                    plan.Removes.Add(rule);
                }
            }

            plan.Adds.Add(expectedRule);
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildServiceRepairPlanAsync(
        string serviceInstanceId,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        config.Normalize();
        var service = config.FindService(serviceInstanceId)
            ?? throw new InvalidOperationException($"Git service '{serviceInstanceId}' was not found.");
        var prefixes = _providers.Get(service.ProviderKind).GetSupportedRemotePatterns(service)
            .Select(item => item.Prefix)
            .ToList();
        var expected = BuildExpectedEntries(config)
            .Where(item => string.Equals(item.Service.Id, service.Id, StringComparison.OrdinalIgnoreCase)
                && !IsLegacyAccountOwnerEntry(item))
            .Select(item => item.Rule)
            .ToList();
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = BuildReconcilePlan(expected, actual);
        var legacy = await BuildLegacyAccountOwnerMigrationPlanAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rule in legacy.Removes.Where(rule => prefixes.Any(prefix =>
                     rule.InsteadOfUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            AddUnique(plan.Removes, rule);
        }

        foreach (var rule in legacy.Adds.Where(rule => prefixes.Any(prefix =>
                     rule.InsteadOfUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            AddUnique(plan.Adds, rule);
        }

        foreach (var routeId in legacy.RepositoryRouteIdsToRemove.Where(routeId => config.RepositoryRoutes.Any(route =>
                     string.Equals(route.Id, routeId, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(route.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))))
        {
            plan.RepositoryRouteIdsToRemove.Add(routeId);
        }

        return plan;
    }

    public async Task<GitRewritePlan> BuildLegacyAccountOwnerMigrationPlanAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        config.Normalize();
        var plan = new GitRewritePlan();
        foreach (var service in config.GitServices.Where(item => item.ProviderKind == GitProviderKind.Gitea
                     && !string.IsNullOrWhiteSpace(item.DefaultIdentityId)))
        {
            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, service.DefaultIdentityId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase));
            if (identity is null || string.IsNullOrWhiteSpace(identity.AccountName))
            {
                continue;
            }

            var legacyConfigRoutes = config.RepositoryRoutes.Where(item => item.Enabled
                    && item.Scope == GitRouteScope.Owner
                    && string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.IdentityId, identity.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Owner ?? item.NamespacePath, identity.AccountName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var route in legacyConfigRoutes)
            {
                plan.RepositoryRouteIdsToRemove.Add(route.Id);
            }

            var legacyRoute = new RepositoryRoute
            {
                ServiceInstanceId = service.Id,
                IdentityId = identity.Id,
                Scope = GitRouteScope.Owner,
                Owner = identity.AccountName
            };
            var detected = _providers.Get(service.ProviderKind).BuildRewriteRules(service, identity, legacyRoute)
                .Where(rule => actual.Any(item => RuleEquals(item, rule)))
                .ToList();
            if (detected.Count == 0 && legacyConfigRoutes.Count == 0)
            {
                continue;
            }

            foreach (var rule in detected)
            {
                if (!plan.Removes.Any(item => RuleEquals(item, rule)))
                {
                    plan.Removes.Add(rule);
                }
            }

            var serviceRoute = new RepositoryRoute
            {
                ServiceInstanceId = service.Id,
                IdentityId = identity.Id,
                Scope = GitRouteScope.Service
            };
            foreach (var rule in _providers.Get(service.ProviderKind).BuildRewriteRules(service, identity, serviceRoute))
            {
                if (!actual.Any(item => RuleEquals(item, rule)) && !plan.Adds.Any(item => RuleEquals(item, rule)))
                {
                    plan.Adds.Add(rule);
                }
            }
        }

        return plan;
    }

    public async Task<OperationResult<IReadOnlyList<ProcessResult>>> ApplyPlanAsync(
        GitRewritePlan plan,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!plan.HasChanges)
        {
            return OperationResult<IReadOnlyList<ProcessResult>>.Ok([], "Git URL rewrite rules already match.");
        }

        var reconciliations = new List<(string Key, IReadOnlyList<string> Desired)>();
        var keys = plan.Adds.Concat(plan.Removes)
            .Select(item => item.ConfigKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var current = await _store.GetValuesAsync(key, cancellationToken).ConfigureAwait(false);
            var removed = plan.Removes.Where(item => string.Equals(item.ConfigKey, key, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.InsteadOfUrl)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var desired = current.Where(value => !removed.Contains(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var value in plan.Adds.Where(item => string.Equals(item.ConfigKey, key, StringComparison.OrdinalIgnoreCase))
                         .Select(item => item.InsteadOfUrl))
            {
                if (!desired.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    desired.Add(value);
                }
            }

            if (!current.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase))
            {
                reconciliations.Add((key, desired));
            }
        }

        if (reconciliations.Count == 0 && plan.RepositoryRouteIdsToRemove.Count == 0)
        {
            return OperationResult<IReadOnlyList<ProcessResult>>.Ok([], "Git URL rewrite rules already match.");
        }

        await _backupService.CreateSnapshotAsync(reason, cancellationToken).ConfigureAwait(false);
        var results = new List<ProcessResult>();
        foreach (var reconciliation in reconciliations)
        {
            var unset = await _store.RemoveAllForKeyAsync(reconciliation.Key, cancellationToken).ConfigureAwait(false);
            results.Add(unset);
            if (!unset.Succeeded && unset.ExitCode is not (1 or 5))
            {
                return OperationResult<IReadOnlyList<ProcessResult>>.Fail("Failed while resetting a managed Git URL rewrite key.", unset.StandardError);
            }

            var baseUrl = reconciliation.Key[4..^".insteadOf".Length];
            foreach (var value in reconciliation.Desired)
            {
                var add = await _store.AddAsync(new GitUrlRewriteRule(baseUrl, value), cancellationToken).ConfigureAwait(false);
                results.Add(add);
                if (!add.Succeeded)
                {
                    return OperationResult<IReadOnlyList<ProcessResult>>.Fail("Failed while adding a Git URL rewrite.", add.StandardError);
                }
            }
        }

        if (plan.RepositoryRouteIdsToRemove.Count > 0)
        {
            var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            config.RepositoryRoutes.RemoveAll(route => plan.RepositoryRouteIdsToRemove.Contains(route.Id, StringComparer.OrdinalIgnoreCase));
            config.Normalize();
            await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult<IReadOnlyList<ProcessResult>>.Ok(results, "Git URL rewrite rules were reconciled idempotently.");
    }

    public async Task<UrlRewritePreview> PreviewAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = BuildCurrentExpectedRules(config);
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var actualMatch = FindLongestMatch(originalUrl, actual);
        var expectedMatch = FindLongestMatch(originalUrl, expected);
        var displayMatch = expectedMatch ?? actualMatch;
        var expectedApplied = expectedMatch is not null && actual.Any(item => RuleEquals(item, expectedMatch));
        var expectedPrefixTargets = expectedMatch is null
            ? new List<GitUrlRewriteRule>()
            : actual.Where(item => string.Equals(item.InsteadOfUrl, expectedMatch.InsteadOfUrl, StringComparison.OrdinalIgnoreCase)).ToList();
        var expectationStatus = expectedMatch is null
            ? UrlRewriteExpectationStatus.None
            : expectedApplied
                ? UrlRewriteExpectationStatus.Applied
                : expectedPrefixTargets.Count > 0
                    ? UrlRewriteExpectationStatus.Conflict
                    : UrlRewriteExpectationStatus.Missing;

        return new UrlRewritePreview
        {
            OriginalUrl = originalUrl,
            MatchedPrefix = displayMatch?.InsteadOfUrl,
            MatchedBaseUrl = displayMatch?.BaseUrl,
            RewrittenUrl = Rewrite(originalUrl, displayMatch),
            ActualMatchedPrefix = actualMatch?.InsteadOfUrl,
            ActualMatchedBaseUrl = actualMatch?.BaseUrl,
            ActualRewrittenUrl = actualMatch is null ? null : Rewrite(originalUrl, actualMatch),
            ExpectedMatchedPrefix = expectedMatch?.InsteadOfUrl,
            ExpectedMatchedBaseUrl = expectedMatch?.BaseUrl,
            ExpectedRewrittenUrl = expectedMatch is null ? null : Rewrite(originalUrl, expectedMatch),
            ExpectationStatus = expectationStatus
        };
    }

    public Task<ProcessResult> TestRemoteAsync(string originalUrl, CancellationToken cancellationToken = default)
        => _store.TestRemoteAsync(originalUrl, cancellationToken);

    public async Task<GitRemoteUrlMatch?> ParseRemoteUrlAsync(
        string remoteUrl,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return _remoteUrlParser.Parse(remoteUrl, config.GitServices);
    }

    private IReadOnlyList<ExpectedRouteRule> BuildExpectedEntries(AppConfig config)
    {
        config.Normalize();
        var result = new List<ExpectedRouteRule>();
        foreach (var route in config.RepositoryRoutes.Where(item => item.Enabled))
        {
            var service = config.FindService(route.ServiceInstanceId);
            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            if (service is null || identity is null
                || !string.Equals(identity.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var rule in _providers.Get(service.ProviderKind).BuildRewriteRules(service, identity, route))
            {
                result.Add(new ExpectedRouteRule(route, service, identity, rule));
            }
        }

        return result;
    }

    private IReadOnlyList<GitUrlRewriteRule> BuildCurrentExpectedRules(AppConfig config)
        => BuildExpectedEntries(config)
            .Where(item => !IsLegacyAccountOwnerEntry(item))
            .Select(item => item.Rule)
            .ToList();

    private static GitRewritePlan BuildReconcilePlan(
        IReadOnlyList<GitUrlRewriteRule> expected,
        IReadOnlyList<GitUrlRewriteRule> actual)
    {
        var plan = new GitRewritePlan();
        foreach (var expectedRule in expected)
        {
            var samePrefix = actual.Where(item =>
                    string.Equals(item.InsteadOfUrl, expectedRule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var exactCount = samePrefix.Count(item => RuleEquals(item, expectedRule));
            if (exactCount == 1 && samePrefix.Count == 1)
            {
                continue;
            }

            foreach (var rule in samePrefix)
            {
                AddUnique(plan.Removes, rule);
            }

            AddUnique(plan.Adds, expectedRule);
        }

        return plan;
    }

    private static void AddUnique(List<GitUrlRewriteRule> rules, GitUrlRewriteRule rule)
    {
        if (!rules.Any(item => RuleEquals(item, rule)))
        {
            rules.Add(rule);
        }
    }

    private static bool RuleEquals(GitUrlRewriteRule left, GitUrlRewriteRule right)
        => string.Equals(left.BaseUrl, right.BaseUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.InsteadOfUrl, right.InsteadOfUrl, StringComparison.OrdinalIgnoreCase);

    private static GitUrlRewriteRule? FindLongestMatch(string originalUrl, IEnumerable<GitUrlRewriteRule> rules)
        => rules.Where(rule => originalUrl.StartsWith(rule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rule => rule.InsteadOfUrl.Length)
            .FirstOrDefault();

    private static string Rewrite(string originalUrl, GitUrlRewriteRule? rule)
        => rule is null ? originalUrl : rule.BaseUrl + originalUrl[rule.InsteadOfUrl.Length..];

    private static bool IsLegacyAccountOwnerEntry(ExpectedRouteRule entry)
        => entry.Service.ProviderKind == GitProviderKind.Gitea
            && entry.Route.Scope == GitRouteScope.Owner
            && string.Equals(entry.Service.DefaultIdentityId, entry.Identity.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Route.Owner ?? entry.Route.NamespacePath, entry.Identity.AccountName, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<GitUrlRewriteRule> FindLegacyAccountOwnerRules(
        AppConfig config,
        IReadOnlyList<GitUrlRewriteRule> actual)
    {
        var result = new List<GitUrlRewriteRule>();
        foreach (var service in config.GitServices.Where(item => item.ProviderKind == GitProviderKind.Gitea
                     && !string.IsNullOrWhiteSpace(item.DefaultIdentityId)))
        {
            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, service.DefaultIdentityId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase));
            if (identity is null || string.IsNullOrWhiteSpace(identity.AccountName))
            {
                continue;
            }

            var legacyRoute = new RepositoryRoute
            {
                ServiceInstanceId = service.Id,
                IdentityId = identity.Id,
                Scope = GitRouteScope.Owner,
                Owner = identity.AccountName
            };
            foreach (var expectedLegacy in _providers.Get(service.ProviderKind).BuildRewriteRules(service, identity, legacyRoute))
            {
                result.AddRange(actual.Where(item => RuleEquals(item, expectedLegacy)));
            }
        }

        return result;
    }

    private sealed record ExpectedRouteRule(
        RepositoryRoute Route,
        GitServiceInstance Service,
        GitIdentity Identity,
        GitUrlRewriteRule Rule);
}
