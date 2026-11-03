using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Core.Validation;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GiteeTemplateTests
{
    [Fact]
    public async Task GiteeTemplateCanBeSavedWithoutChangingBuiltInServices()
    {
        var template = GitServiceService.CreateTemplate("Gitee");
        Assert.Contains("Gitee", GitServiceService.AvailableTemplates);
        Assert.Equal("gitee.com", template.Id);
        Assert.Equal("gitee.com", template.HostName);
        Assert.Equal("https://gitee.com", template.WebBaseUrl);
        Assert.Equal("git", template.SshUser);
        Assert.Equal(22, template.SshPort);
        Assert.Equal(GitProviderKind.Generic, template.ProviderKind);
        Assert.False(template.IsBuiltIn);
        Assert.False(template.AllowInsecureHttp);
        Assert.True(GitServiceValidator.Validate(template, new AppConfig()).IsValid);

        var store = new InMemoryAppConfigStore();
        var backups = new NoOpBackupService();
        var service = new GitServiceService(store, backups,
            new StubProcessRunner(_ => throw new InvalidOperationException("No network expected.")),
            new FixedToolchainService("git.exe"), GitProviderAdapterRegistry.CreateDefault());
        Assert.True((await service.SaveAsync(template)).Success);
        Assert.True((await service.SaveAsync(GitServiceService.CreateTemplate("Gitee"))).Success);
        Assert.Single(store.Config.GitServices, item => item.Id == "gitee.com");
        Assert.NotNull(store.Config.FindService(GitServiceInstance.GitHubComId));
    }

    [Theory]
    [InlineData("https://gitee.com/team/repo.git")]
    [InlineData("git@gitee.com:team/repo.git")]
    [InlineData("ssh://git@gitee.com/team/repo.git")]
    public void GiteeUrlsParseAndRouteUsingGenericAdapter(string url)
    {
        var service = GitServiceService.CreateTemplate("Gitee");
        var match = new GitRemoteUrlParser().Parse(url, [service]);
        Assert.NotNull(match);
        Assert.Equal("team", match.NamespacePath);
        Assert.Equal("repo", match.RepositoryName);
        var identity = new GitIdentity { HostAlias = "gitee-work", PrivateKeyPath = "C:/fake/keys/work" };
        var adapter = GitProviderAdapterRegistry.CreateDefault().Get(service.ProviderKind);
        var rules = adapter.BuildRewriteRules(service, identity, new RepositoryRoute { NamespacePath = "team" });
        var matchingRule = Assert.Single(rules, rule => url.StartsWith(rule.InsteadOfUrl, StringComparison.Ordinal));
        Assert.Equal("git@gitee-work:team/", matchingRule.BaseUrl);
        var block = adapter.BuildSshManagedBlock(service, identity, "\n");
        Assert.Contains("HostName gitee.com", block);
        Assert.Contains("IdentitiesOnly yes", block);
        Assert.DoesNotContain("ssh-rsa", block);
    }

    [Theory]
    [InlineData("Hi demo! You've successfully authenticated, but Gitee.com does not provide shell access.", true)]
    [InlineData("Permission denied (publickey).", false)]
    public void GiteeAuthenticationUsesExistingGreetingRecognition(string output, bool expected)
    {
        var adapter = GitProviderAdapterRegistry.CreateDefault().Get(GitServiceService.CreateTemplate("Gitee").ProviderKind);
        Assert.Equal(expected, adapter.IsAuthenticationSuccess(new ProcessResult
        {
            ExecutablePath = "ssh.exe",
            ExitCode = 1,
            StandardError = output
        }));
    }
}
