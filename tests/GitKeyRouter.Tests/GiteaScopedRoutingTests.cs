using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Core.Validation;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GiteaScopedRoutingTests
{
    private const string SharedPrivateKey = @"C:\Users\fgc01\.ssh\id_ed25519_gitea";
    private const string SharedPublicKey = SharedPrivateKey + ".pub";

    [Theory]
    [InlineData("Gitea Local", "gitea-local", "gitea.lan.policoil.top", "https://gitea.lan.policoil.top/")]
    [InlineData("Gitea Cloud", "gitea-cloud", "git.policoil.top", "https://git.policoil.top/")]
    public void ServiceRoute_GeneratesScpSshGitSshAndHttpsPrefixes(string template, string alias, string host, string httpsPrefix)
    {
        var service = GitServiceService.CreateTemplate(template);
        var identity = Identity(alias, service.Id, alias);
        var route = Route(service, identity, GitRouteScope.Service);

        var rules = GitProviderAdapterRegistry.CreateDefault().Get(GitProviderKind.Gitea)
            .BuildRewriteRules(service, identity, route);

        Assert.Equal(4, rules.Count);
        Assert.All(rules, rule => Assert.Equal($"git@{alias}:", rule.BaseUrl));
        Assert.Contains(rules, rule => rule.InsteadOfUrl == $"git@{host}:");
        Assert.Contains(rules, rule => rule.InsteadOfUrl == $"ssh://git@{host}/");
        Assert.Contains(rules, rule => rule.InsteadOfUrl == $"git+ssh://git@{host}/");
        Assert.Contains(rules, rule => rule.InsteadOfUrl == httpsPrefix);
        Assert.Equal(22, service.SshPort);
    }

    [Fact]
    public async Task DefaultGiteaIdentities_CreateOnlyServiceRoutesAndMayShareKeys()
    {
        var local = GitServiceService.CreateTemplate("Gitea Local");
        var cloud = GitServiceService.CreateTemplate("Gitea Cloud");
        var localIdentity = Identity("gitea-local", local.Id, "gitea-local");
        var cloudIdentity = Identity("gitea-cloud", cloud.Id, "gitea-cloud");
        local.DefaultIdentityId = localIdentity.Id;
        cloud.DefaultIdentityId = cloudIdentity.Id;
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig { Identities = [localIdentity, cloudIdentity] }
        };
        var manager = CreateServiceManager(store);

        Assert.True((await manager.SaveAsync(local)).Success);
        Assert.True((await manager.SaveAsync(cloud)).Success);

        Assert.Equal(2, store.Config.RepositoryRoutes.Count);
        Assert.All(store.Config.RepositoryRoutes, route => Assert.Equal(GitRouteScope.Service, route.Scope));
        Assert.DoesNotContain(store.Config.RepositoryRoutes, route => route.Scope == GitRouteScope.Owner);
        Assert.DoesNotContain(store.Config.RepositoryRoutes, route => string.Equals(route.Owner, "fgc0109", StringComparison.OrdinalIgnoreCase));
        Assert.True(IdentityValidator.Validate(localIdentity, store.Config).IsValid);
        Assert.True(IdentityValidator.Validate(cloudIdentity, store.Config).IsValid);
    }

    [Fact]
    public void SharedGiteaKey_ProducesIndependentPort22SshBlocks()
    {
        var local = GitServiceService.CreateTemplate("Gitea Local");
        var cloud = GitServiceService.CreateTemplate("Gitea Cloud");
        var adapter = GitProviderAdapterRegistry.CreateDefault().Get(GitProviderKind.Gitea);

        var localBlock = adapter.BuildSshManagedBlock(local, Identity("gitea-local", local.Id, "gitea-local"), "\n");
        var cloudBlock = adapter.BuildSshManagedBlock(cloud, Identity("gitea-cloud", cloud.Id, "gitea-cloud"), "\n");

        Assert.Contains("Host gitea-local\n", localBlock, StringComparison.Ordinal);
        Assert.Contains("HostName gitea.lan.policoil.top", localBlock, StringComparison.Ordinal);
        Assert.Contains("Host gitea-cloud\n", cloudBlock, StringComparison.Ordinal);
        Assert.Contains("HostName git.policoil.top", cloudBlock, StringComparison.Ordinal);
        Assert.Contains("User git", localBlock, StringComparison.Ordinal);
        Assert.Contains("Port 22", localBlock, StringComparison.Ordinal);
        Assert.Contains("Port 22", cloudBlock, StringComparison.Ordinal);
        Assert.Contains("IdentityFile C:/Users/fgc01/.ssh/id_ed25519_gitea", localBlock, StringComparison.Ordinal);
        Assert.Contains("IdentityFile C:/Users/fgc01/.ssh/id_ed25519_gitea", cloudBlock, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongestPrefix_UsesRepositoryThenOwnerThenService()
    {
        var service = GitServiceService.CreateTemplate("Gitea Cloud");
        var defaultIdentity = Identity("gitea-cloud", service.Id, "gitea-cloud");
        var ownerIdentity = Identity("gitea-special", service.Id, "gitea-special");
        var repositoryIdentity = Identity("gitea-private", service.Id, "gitea-private");
        var routes = new[]
        {
            Route(service, defaultIdentity, GitRouteScope.Service),
            Route(service, ownerIdentity, GitRouteScope.Owner, "special-org"),
            Route(service, repositoryIdentity, GitRouteScope.Repository, "special-org", "private-repo.git")
        };
        var git = new FakeGitUrlRewriteStore();
        foreach (var route in routes)
        {
            var identity = route.IdentityId == defaultIdentity.Id ? defaultIdentity : route.IdentityId == ownerIdentity.Id ? ownerIdentity : repositoryIdentity;
            git.Rules.AddRange(GitProviderAdapterRegistry.CreateDefault().Get(GitProviderKind.Gitea).BuildRewriteRules(service, identity, route));
        }

        var rewrites = new GitUrlRewriteService(new InMemoryAppConfigStore(), git, new NoOpBackupService());
        Assert.Equal("git@gitea-cloud:project-base/proto-tool-pb-extra.git",
            (await rewrites.PreviewAsync("https://git.policoil.top/project-base/proto-tool-pb-extra.git")).RewrittenUrl);
        Assert.Equal("git@gitea-special:special-org/server.git",
            (await rewrites.PreviewAsync("https://git.policoil.top/special-org/server.git")).RewrittenUrl);
        Assert.Equal("git@gitea-private:special-org/private-repo.git",
            (await rewrites.PreviewAsync("https://git.policoil.top/special-org/private-repo.git")).RewrittenUrl);
    }

    [Fact]
    public void GitHub_KeepsOwnerRoutesAndRejectsServiceScope()
    {
        var github = GitServiceInstance.CreateGitHubCom();
        var camus = Identity("github-camus", github.Id, "github-camus", "camus0109");
        var fgc = Identity("github-fgc", github.Id, "github-fgc", "fgc0109");
        var config = new AppConfig
        {
            Identities = [camus, fgc],
            RepositoryRoutes = [Route(github, camus, GitRouteScope.Owner, "camus0109"), Route(github, fgc, GitRouteScope.Owner, "fgc0109")]
        };
        var rules = OwnerRouteService.BuildExpectedRules(config);

        Assert.Contains(rules, rule => rule.BaseUrl == "git@github-camus:camus0109/");
        Assert.Contains(rules, rule => rule.BaseUrl == "git@github-fgc:fgc0109/");
        Assert.DoesNotContain(rules, rule => rule.InsteadOfUrl == "https://github.com/");
        Assert.False(OwnerRouteValidator.Validate(Route(github, camus, GitRouteScope.Service), config, null, null).IsValid);
    }

    [Fact]
    public async Task Reconcile_IsIdempotentAndPreservesUnrelatedValuesUnderManagedKey()
    {
        var store = new FakeGitUrlRewriteStore();
        var desired = new GitUrlRewriteRule("git@gitea-cloud:", "https://git.policoil.top/");
        var unrelated = new GitUrlRewriteRule("git@gitea-cloud:", "https://manual.example/");
        store.Rules.Add(desired);
        store.Rules.Add(desired);
        store.Rules.Add(unrelated);
        var backup = new NoOpBackupService();
        var service = new GitUrlRewriteService(new InMemoryAppConfigStore(), store, backup);
        var plan = new GitRewritePlan();
        plan.Removes.Add(desired);
        plan.Adds.Add(desired);

        var first = await service.ApplyPlanAsync(plan, "deduplicate");
        var second = await service.ApplyPlanAsync(plan, "deduplicate again");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Single(store.Rules, rule => rule == desired);
        Assert.Contains(unrelated, store.Rules);
        Assert.Equal(1, backup.SnapshotCount);
        Assert.Empty(second.Value ?? []);
    }

    [Fact]
    public async Task LegacyAccountOwnerMigration_IsPreviewOnlyUntilApplied()
    {
        var service = GitServiceService.CreateTemplate("Gitea Cloud");
        var identity = Identity("gitea-cloud", service.Id, "gitea-cloud");
        service.DefaultIdentityId = identity.Id;
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), service],
            Identities = [identity]
        };
        var store = new FakeGitUrlRewriteStore();
        store.Rules.AddRange(GitProviderAdapterRegistry.CreateDefault().Get(GitProviderKind.Gitea)
            .BuildRewriteRules(service, identity, Route(service, identity, GitRouteScope.Owner, identity.AccountName)));
        var rewrites = new GitUrlRewriteService(new InMemoryAppConfigStore { Config = config }, store, new NoOpBackupService());

        var plan = await rewrites.BuildLegacyAccountOwnerMigrationPlanAsync();

        Assert.Equal(4, plan.Removes.Count);
        Assert.Equal(4, plan.Adds.Count);
        Assert.Contains(store.Rules, rule => rule.InsteadOfUrl.EndsWith("/fgc0109/", StringComparison.Ordinal));
        Assert.True((await rewrites.ApplyPlanAsync(plan, "confirm migration")).Success);
        Assert.DoesNotContain(store.Rules, rule => rule.InsteadOfUrl.EndsWith("/fgc0109/", StringComparison.Ordinal));
        Assert.Contains(store.Rules, rule => rule.InsteadOfUrl == "git@git.policoil.top:");
        Assert.Contains(store.Rules, rule => rule.InsteadOfUrl == "https://git.policoil.top/");
    }

    private static GitIdentity Identity(string id, string serviceId, string alias, string account = "fgc0109")
        => new()
        {
            Id = id,
            ServiceInstanceId = serviceId,
            DisplayName = id,
            AccountName = account,
            HostAlias = alias,
            PrivateKeyPath = SharedPrivateKey,
            PublicKeyPath = SharedPublicKey
        };

    private static RepositoryRoute Route(GitServiceInstance service, GitIdentity identity, GitRouteScope scope, string? owner = null, string? repository = null)
    {
        var route = new RepositoryRoute
        {
            ServiceInstanceId = service.Id,
            IdentityId = identity.Id,
            Scope = scope,
            Owner = owner,
            Repository = repository
        };
        route.Normalize();
        return route;
    }

    private static GitServiceService CreateServiceManager(InMemoryAppConfigStore store)
        => new(
            store,
            new NoOpBackupService(),
            new StubProcessRunner(_ => new ProcessResult { ExecutablePath = "ssh.exe" }),
            new FixedToolchainService("git.exe", sshPath: "ssh.exe"),
            GitProviderAdapterRegistry.CreateDefault());
}
