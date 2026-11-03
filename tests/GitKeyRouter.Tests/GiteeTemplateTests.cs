using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Core.Validation;
using GitKeyRouter.Tests.TestSupport;
using GitKeyRouter.Infrastructure.Configuration;
using GitKeyRouter.Infrastructure.FileSystem;

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
        Assert.Equal(GitProviderKind.Gitee, template.ProviderKind);
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
    [InlineData("https://gitee.com/team/repo.git", GitProviderKind.Gitee)]
    [InlineData("git@gitee.com:team/repo.git", GitProviderKind.Gitee)]
    [InlineData("ssh://git@gitee.com/team/repo.git", GitProviderKind.Gitee)]
    [InlineData("https://gitee.com/team/repo.git", GitProviderKind.Generic)]
    [InlineData("git@gitee.com:team/repo.git", GitProviderKind.Generic)]
    [InlineData("ssh://git@gitee.com/team/repo.git", GitProviderKind.Generic)]
    public void NewAndLegacyGiteeUrlsParseAndRoute(string url, GitProviderKind kind)
    {
        var service = GitServiceService.CreateTemplate("Gitee");
        service.ProviderKind = kind;
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

    [Theory]
    [InlineData("\"Generic\"")]
    [InlineData("3")]
    [InlineData("\"Gitee\"")]
    [InlineData("4")]
    public async Task NewAndLegacyConfigurationRoundTripsWithoutChangingReferences(string providerJson)
    {
        using var directory = new TemporaryDirectory();
        var paths = new TestAppPaths(directory.Path);
        Directory.CreateDirectory(paths.AppDataDirectory);
        var original = $$"""
            {
              "SchemaVersion": 4,
              "GitServices": [{ "Id": "gitea-local", "DisplayName": "Gitee", "ProviderKind": {{providerJson}},
                "HostName": " GITEE.COM ", "WebBaseUrl": "https://gitee.com", "DefaultIdentityId": "work" }],
              "Identities": [{ "Id": "work", "ServiceInstanceId": "gitea-local", "HostAlias": "gitee-work", "PrivateKeyPath": "mock-key" }],
              "RepositoryRoutes": [{ "Id": "team", "ServiceInstanceId": "gitea-local", "IdentityId": "work", "NamespacePath": "team" }],
              "GitProfiles": [{ "Id": "work-profile", "DefaultServiceInstanceId": "gitea-local" }]
            }
            """;
        await File.WriteAllTextAsync(paths.ConfigPath, original);
        var store = new JsonAppConfigStore(paths, new PhysicalFileSystem());

        var config = await store.LoadAsync();
        Assert.Equal(original, await File.ReadAllTextAsync(paths.ConfigPath)); // Read does not persist migration.
        var service = Assert.Single(config.GitServices, item => item.Id == "gitea-local");
        Assert.Equal(GitProviderKind.Gitee, service.ProviderKind);
        Assert.Equal("work", service.DefaultIdentityId);
        Assert.Equal("gitea-local", Assert.Single(config.Identities).ServiceInstanceId);
        Assert.Equal("gitee-work", config.Identities[0].HostAlias);
        Assert.Equal("mock-key", config.Identities[0].PrivateKeyPath);
        Assert.All(config.RepositoryRoutes, route => Assert.Equal("gitea-local", route.ServiceInstanceId));
        Assert.Equal("gitea-local", Assert.Single(config.GitProfiles).DefaultServiceInstanceId);
        await store.SaveAsync(config);
        var saved = await File.ReadAllTextAsync(paths.ConfigPath);
        Assert.Contains("\"ProviderKind\": \"Gitee\"", saved);
        Assert.Equal(GitProviderKind.Gitee, (await store.LoadAsync()).FindService("gitea-local")?.ProviderKind);
    }

    [Theory]
    [InlineData("gitee.com.example.test", GitProviderKind.Generic)]
    [InlineData("git.gitee.com", GitProviderKind.Generic)]
    [InlineData("example.test", GitProviderKind.Generic)]
    [InlineData("gitee.com", GitProviderKind.Gitea)]
    public void LegacyRecognitionDoesNotReclassifyOtherServices(string host, GitProviderKind kind)
    {
        var service = new GitServiceInstance { Id = "custom", HostName = host, ProviderKind = kind };
        var config = new AppConfig { GitServices = [service] };
        config.Normalize();
        Assert.Equal(kind, service.ProviderKind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GiteeKeepsGenericRewriteAndSshBehavior(bool extended, bool insecureHttp)
    {
        var service = GitServiceService.CreateTemplate("Gitee");
        service.EnableExtendedSshUrlRewrites = extended;
        service.AllowInsecureHttp = insecureHttp;
        var registry = GitProviderAdapterRegistry.CreateDefault();
        var identity = new GitIdentity { HostAlias = "gitee-work", PrivateKeyPath = "mock-key" };
        var route = new RepositoryRoute { Scope = GitRouteScope.Owner, NamespacePath = "team" };
        var rules = registry.Get(GitProviderKind.Gitee).BuildRewriteRules(service, identity, route);
        var ssh = registry.Get(GitProviderKind.Gitee).BuildSshManagedBlock(service, identity, "\n");
        service.ProviderKind = GitProviderKind.Generic;
        Assert.Equal(registry.Get(GitProviderKind.Generic).BuildRewriteRules(service, identity, route), rules);
        Assert.Equal(registry.Get(GitProviderKind.Generic).BuildSshManagedBlock(service, identity, "\n"), ssh);
        if (!extended)
        {
            Assert.DoesNotContain(rules, rule => rule.InsteadOfUrl.StartsWith("ssh://", StringComparison.Ordinal));
            Assert.DoesNotContain(rules, rule => rule.InsteadOfUrl.StartsWith("git+ssh://", StringComparison.Ordinal));
        }
        Assert.DoesNotContain("Port 22", ssh); // Gitea's special behavior must not leak into Gitee.
    }

}
