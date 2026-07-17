using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GitUrlRewriteServiceTests
{
    [Fact]
    public async Task Compare_DetectsDuplicateAndConflict()
    {
        var configStore = ConfigStore();
        var git = new FakeGitUrlRewriteStore();
        git.Rules.Add(new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/"));
        git.Rules.Add(new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/"));
        git.Rules.Add(new GitUrlRewriteRule("git@wrong:camus0109/", "git@github.com:camus0109/"));
        var service = new GitUrlRewriteService(configStore, git, new NoOpBackupService());

        var comparison = await service.CompareAsync();

        Assert.Contains(comparison, item => item.InsteadOfUrl.StartsWith("https://", StringComparison.Ordinal) && item.Status == GitRewriteStatus.Duplicate);
        Assert.Contains(comparison, item => item.InsteadOfUrl.StartsWith("git@", StringComparison.Ordinal) && item.Status == GitRewriteStatus.Conflict);
    }

    [Fact]
    public async Task Preview_UsesLongestMatchingPrefix()
    {
        var configStore = ConfigStore();
        var git = new FakeGitUrlRewriteStore();
        git.Rules.Add(new GitUrlRewriteRule("git@generic:", "https://github.com/"));
        git.Rules.Add(new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/"));
        var service = new GitUrlRewriteService(configStore, git, new NoOpBackupService());

        var preview = await service.PreviewAsync("https://github.com/camus0109/panel-terraria.git");

        Assert.Equal("git@github-camus:camus0109/panel-terraria.git", preview.RewrittenUrl);
    }

    [Fact]
    public async Task CleanupDuplicates_RemovesAllThenAddsOne()
    {
        var configStore = ConfigStore();
        var git = new FakeGitUrlRewriteStore();
        var rule = new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/");
        git.Rules.Add(rule);
        git.Rules.Add(rule);
        var service = new GitUrlRewriteService(configStore, git, new NoOpBackupService());

        var plan = await service.BuildCleanupDuplicatesPlanAsync();
        var result = await service.ApplyPlanAsync(plan, "test");

        Assert.True(result.Success);
        Assert.Single(git.Rules.Where(item => item == rule));
    }

    private static InMemoryAppConfigStore ConfigStore()
        => new()
        {
            Config = new AppConfig
            {
                Identities = [new GitHubIdentity { Id = "camus", DisplayName = "Camus", HostAlias = "github-camus" }],
                OwnerRoutes = [new OwnerRoute { GitHubOwner = "camus0109", IdentityId = "camus", Enabled = true }]
            }
        };
}
