using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class OwnerRouteServiceTests
{
    [Fact]
    public async Task Save_NewDisabledRouteDoesNotOverwriteExistingEnabledRoute()
    {
        var identity = new GitIdentity { Id = "work", HostAlias = "github-work" };
        var active = new RepositoryRoute { Owner = "team", NamespacePath = "team", IdentityId = identity.Id };
        var disabled = new RepositoryRoute { Owner = "team", IdentityId = identity.Id, Enabled = false };
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig { Identities = [identity], RepositoryRoutes = [active] }
        };
        var service = new OwnerRouteService(store, new NoOpBackupService());

        var result = await service.SaveAsync(disabled);

        Assert.True(result.Success);
        Assert.Equal(2, store.Config.RepositoryRoutes.Count);
        Assert.True(active.Enabled);
        Assert.Contains(store.Config.RepositoryRoutes, route => route.Id == disabled.Id && !route.Enabled);
    }

    [Fact]
    public async Task Save_EnablingDisabledDuplicateRejectsConflictEvenWhenOriginalPathMatches()
    {
        var identity = new GitIdentity { Id = "work", HostAlias = "github-work" };
        var active = new RepositoryRoute { Owner = "team", NamespacePath = "team", IdentityId = identity.Id };
        var disabled = new RepositoryRoute { Owner = "team", NamespacePath = "team", IdentityId = identity.Id, Enabled = false };
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig { Identities = [identity], RepositoryRoutes = [active, disabled] }
        };
        var backup = new NoOpBackupService();
        var service = new OwnerRouteService(store, backup);
        var edited = new RepositoryRoute { Id = disabled.Id, Owner = "team", IdentityId = identity.Id, Enabled = true };

        var result = await service.SaveAsync(edited, disabled.ServiceInstanceId, disabled.NamespacePath);

        Assert.False(result.Success);
        Assert.False(disabled.Enabled);
        Assert.True(active.Enabled);
        Assert.Equal(0, backup.SnapshotCount);
    }

    [Fact]
    public async Task Save_LegacyRenameUsesExplicitOriginalPath()
    {
        var identity = new GitIdentity { Id = "work", HostAlias = "github-work" };
        var original = new RepositoryRoute { Owner = "old-team", NamespacePath = "old-team", IdentityId = identity.Id };
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig { Identities = [identity], RepositoryRoutes = [original] }
        };
        var service = new OwnerRouteService(store, new NoOpBackupService());

        var result = await service.SaveAsync(new RepositoryRoute { Owner = "new-team", IdentityId = identity.Id }, "old-team");

        Assert.True(result.Success);
        Assert.Equal("new-team", Assert.Single(store.Config.RepositoryRoutes).NamespacePath);
    }

    [Fact]
    public void BuildExpectedRules_GeneratesHttpsAndSshRules()
    {
        var config = new AppConfig
        {
            Identities =
            [
                new GitIdentity
                {
                    Id = "camus",
                    DisplayName = "Camus GitHub",
                    ServiceInstanceId = GitServiceInstance.GitHubComId,
                    AccountName = "camus0109",
                    HostAlias = "github-camus",
                    PrivateKeyPath = @"C:\keys\camus",
                    PublicKeyPath = @"C:\keys\camus.pub"
                }
            ],
            RepositoryRoutes =
            [
                new RepositoryRoute
                {
                    ServiceInstanceId = GitServiceInstance.GitHubComId,
                    Scope = GitRouteScope.Owner,
                    Owner = "camus0109",
                    IdentityId = "camus",
                    Enabled = true
                }
            ]
        };

        var rules = OwnerRouteService.BuildExpectedRules(config);

        Assert.Contains(rules, rule => rule.BaseUrl == "git@github-camus:camus0109/"
            && rule.InsteadOfUrl == "https://github.com/camus0109/");
        Assert.Contains(rules, rule => rule.BaseUrl == "git@github-camus:camus0109/"
            && rule.InsteadOfUrl == "git@github.com:camus0109/");
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void BuildExpectedRules_IgnoresDisabledRoutes()
    {
        var config = new AppConfig
        {
            Identities =
            [
                new GitIdentity
                {
                    Id = "one",
                    ServiceInstanceId = GitServiceInstance.GitHubComId,
                    HostAlias = "github-one"
                }
            ],
            RepositoryRoutes =
            [
                new RepositoryRoute
                {
                    ServiceInstanceId = GitServiceInstance.GitHubComId,
                    Scope = GitRouteScope.Owner,
                    Owner = "owner",
                    IdentityId = "one",
                    Enabled = false
                }
            ]
        };

        Assert.Empty(OwnerRouteService.BuildExpectedRules(config));
    }

    [Fact]
    public void BuildExpectedRules_GeneratesNestedGitLabNamespaceRules()
    {
        var gitLab = new GitServiceInstance
        {
            Id = "gitlab-office",
            DisplayName = "Office GitLab",
            ProviderKind = GitProviderKind.GitLab,
            HostName = "gitlab.office.example",
            SshPort = 2222,
            SshUser = "git",
            WebBaseUrl = "https://gitlab.office.example"
        };
        var identity = new GitIdentity
        {
            Id = "work",
            ServiceInstanceId = gitLab.Id,
            DisplayName = "Work",
            AccountName = "camus",
            HostAlias = "gitlab-work"
        };
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), gitLab],
            Identities = [identity],
            RepositoryRoutes =
            [
                new RepositoryRoute
                {
                    ServiceInstanceId = gitLab.Id,
                    NamespacePath = "company/platform",
                    IdentityId = identity.Id,
                    Enabled = true
                }
            ]
        };

        var rules = OwnerRouteService.BuildExpectedRules(config);

        Assert.Contains(rules, rule => rule.BaseUrl == "git@gitlab-work:company/platform/"
            && rule.InsteadOfUrl == "https://gitlab.office.example/company/platform/");
        Assert.Contains(rules, rule => rule.BaseUrl == "git@gitlab-work:company/platform/"
            && rule.InsteadOfUrl == "git@gitlab.office.example:company/platform/");
    }

    [Fact]
    public void Validator_ScopesDuplicateNamespacesByServiceAndRequiresMatchingIdentity()
    {
        var gitLab = new GitServiceInstance
        {
            Id = "gitlab-office",
            DisplayName = "Office GitLab",
            ProviderKind = GitProviderKind.GitLab,
            HostName = "gitlab.office.example",
            SshUser = "git",
            WebBaseUrl = "https://gitlab.office.example"
        };
        var githubIdentity = new GitIdentity
        {
            Id = "github",
            ServiceInstanceId = GitServiceInstance.GitHubComId,
            DisplayName = "GitHub",
            AccountName = "team",
            HostAlias = "github-team"
        };
        var gitLabIdentity = new GitIdentity
        {
            Id = "gitlab",
            ServiceInstanceId = gitLab.Id,
            DisplayName = "GitLab",
            AccountName = "team",
            HostAlias = "gitlab-team"
        };
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), gitLab],
            Identities = [githubIdentity, gitLabIdentity],
            RepositoryRoutes =
            [
                new RepositoryRoute
                {
                    ServiceInstanceId = GitServiceInstance.GitHubComId,
                    NamespacePath = "team",
                    IdentityId = githubIdentity.Id,
                    Enabled = true
                }
            ]
        };
        var candidate = new RepositoryRoute
        {
            ServiceInstanceId = gitLab.Id,
            NamespacePath = "team",
            IdentityId = gitLabIdentity.Id,
            Enabled = true
        };

        Assert.True(GitKeyRouter.Core.Validation.OwnerRouteValidator.Validate(
            candidate,
            config,
            null,
            null,
            GitProviderAdapterRegistry.CreateDefault()).IsValid);

        candidate.IdentityId = githubIdentity.Id;
        Assert.False(GitKeyRouter.Core.Validation.OwnerRouteValidator.Validate(
            candidate,
            config,
            null,
            null,
            GitProviderAdapterRegistry.CreateDefault()).IsValid);
    }
}
