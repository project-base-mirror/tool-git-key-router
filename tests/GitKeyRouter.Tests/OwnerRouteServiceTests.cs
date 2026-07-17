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
                new GitHubIdentity
                {
                    Id = "camus",
                    DisplayName = "Camus GitHub",
                    GitHubUsername = "camus0109",
                    HostAlias = "github-camus",
                    PrivateKeyPath = @"C:\keys\camus",
                    PublicKeyPath = @"C:\keys\camus.pub"
                }
            ],
            OwnerRoutes = [new OwnerRoute { GitHubOwner = "camus0109", IdentityId = "camus", Enabled = true }]
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
            Identities = [new GitHubIdentity { Id = "one", HostAlias = "github-one" }],
            OwnerRoutes = [new OwnerRoute { GitHubOwner = "owner", IdentityId = "one", Enabled = false }]
        };

        Assert.Empty(OwnerRouteService.BuildExpectedRules(config));
    }
}
