using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.Tests;

public sealed class OwnerRouteServiceTests
{
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
