using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed class GitUrlRewriteService
{
    private readonly IAppConfigStore _configStore;
    private readonly IGitUrlRewriteStore _store;
    private readonly IBackupService _backupService;
    private readonly GitProviderAdapterRegistry _providers;

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
    }

    public async Task<IReadOnlyList<GitRewriteComparison>> CompareAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expectedEntries = BuildExpectedEntries(config);
        var expected = expectedEntries.Select(item => item.Rule).ToList();
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var comparisons = new List<GitRewriteComparison>();

        foreach (var entry in expectedEntries)
        {
            var expectedRule = entry.Rule;
            var exact = actual.Where(item => RuleEquals(item, expectedRule)).ToList();
            var samePrefix = actual.Where(item => string.Equals(item.InsteadOfUrl, expectedRule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase)).ToList();
            var status = exact.Count switch
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
                Status = GitRewriteStatus.Extra,
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
        var expected = OwnerRouteService.BuildExpectedRules(config);
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

    public async Task<GitRewritePlan> BuildRegeneratePlanAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var expected = OwnerRouteService.BuildExpectedRules(config);
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
        var expected = OwnerRouteService.BuildExpectedRules(config);
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

    public async Task<OperationResult<IReadOnlyList<ProcessResult>>> ApplyPlanAsync(
        GitRewritePlan plan,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!plan.HasChanges)
        {
            return OperationResult<IReadOnlyList<ProcessResult>>.Ok([], "Git URL rewrite rules already match.");
        }

        await _backupService.CreateSnapshotAsync(reason, cancellationToken).ConfigureAwait(false);
        var results = new List<ProcessResult>();
        foreach (var rule in plan.Removes)
        {
            var result = await _store.RemoveAllAsync(rule, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (!result.Succeeded && result.ExitCode != 5)
            {
                return OperationResult<IReadOnlyList<ProcessResult>>.Fail("Failed while removing a Git URL rewrite.", result.StandardError);
            }
        }

        foreach (var rule in plan.Adds)
        {
            var result = await _store.AddAsync(rule, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (!result.Succeeded)
            {
                return OperationResult<IReadOnlyList<ProcessResult>>.Fail("Failed while adding a Git URL rewrite.", result.StandardError);
            }
        }

        return OperationResult<IReadOnlyList<ProcessResult>>.Ok(results, "Git URL rewrite rules were updated.");
    }

    public async Task<UrlRewritePreview> PreviewAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        var actual = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var match = actual
            .Where(rule => originalUrl.StartsWith(rule.InsteadOfUrl, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rule => rule.InsteadOfUrl.Length)
            .FirstOrDefault();

        if (match is null)
        {
            return new UrlRewritePreview
            {
                OriginalUrl = originalUrl,
                RewrittenUrl = originalUrl
            };
        }

        return new UrlRewritePreview
        {
            OriginalUrl = originalUrl,
            MatchedPrefix = match.InsteadOfUrl,
            MatchedBaseUrl = match.BaseUrl,
            RewrittenUrl = match.BaseUrl + originalUrl[match.InsteadOfUrl.Length..]
        };
    }

    public Task<ProcessResult> TestRemoteAsync(string originalUrl, CancellationToken cancellationToken = default)
        => _store.TestRemoteAsync(originalUrl, cancellationToken);

    private IReadOnlyList<ExpectedRouteRule> BuildExpectedEntries(AppConfig config)
    {
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

    private static bool RuleEquals(GitUrlRewriteRule left, GitUrlRewriteRule right)
        => string.Equals(left.BaseUrl, right.BaseUrl, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.InsteadOfUrl, right.InsteadOfUrl, StringComparison.OrdinalIgnoreCase);

    private sealed record ExpectedRouteRule(
        RepositoryRoute Route,
        GitServiceInstance Service,
        GitIdentity Identity,
        GitUrlRewriteRule Rule);
}
