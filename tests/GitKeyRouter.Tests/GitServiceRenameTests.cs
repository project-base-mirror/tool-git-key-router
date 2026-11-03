using System.Text.Json;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GitServiceRenameTests
{
    [Fact]
    public async Task RenameUpdatesAllReferencesAndPreservesRoutingAndKeyPaths()
    {
        var config = CreateLegacyConfig();
        var originalJson = JsonSerializer.Serialize(config);
        var expectedRules = OwnerRouteService.BuildExpectedRules(config);
        var store = new InMemoryAppConfigStore { Config = config };
        var backup = new NoOpBackupService();
        var service = CreateService(store, backup);

        var result = await service.RenameAsync("legacy-gitee", "gitee.com");

        Assert.True(result.Success);
        Assert.Null(store.Config.FindService("legacy-gitee"));
        Assert.Equal("Gitee", store.Config.FindService("gitee.com")?.DisplayName);
        var identity = Assert.Single(store.Config.Identities);
        Assert.Equal("gitee.com", identity.ServiceInstanceId);
        Assert.Equal("gitee-work", identity.HostAlias);
        Assert.Equal("C:/fake/keys/gitee", identity.PrivateKeyPath);
        Assert.All(store.Config.RepositoryRoutes, route => Assert.Equal("gitee.com", route.ServiceInstanceId));
        Assert.Contains(store.Config.RepositoryRoutes, route => route.Id == "service-default:gitee.com");
        Assert.Contains(store.Config.RepositoryRoutes, route => route.Id == "team-route");
        var profile = Assert.Single(store.Config.GitProfiles);
        Assert.Equal("gitee.com", profile.DefaultServiceInstanceId);
        Assert.Equal(identity.Id, profile.DefaultIdentityId);
        Assert.Single(store.Config.GitProfileRules);
        Assert.Equal(expectedRules, OwnerRouteService.BuildExpectedRules(store.Config));
        Assert.Equal(originalJson, JsonSerializer.Serialize(config));
        Assert.Equal(1, backup.SnapshotCount);
    }

    [Fact]
    public async Task EditingServiceCanRenameAndUpdateOtherFieldsInOneSave()
    {
        var store = new InMemoryAppConfigStore { Config = CreateLegacyConfig() };
        var backup = new NoOpBackupService();
        var edited = GitServiceService.CreateTemplate("Gitee");
        edited.DisplayName = "Gitee Work";
        edited.DefaultIdentityId = "work";

        var result = await CreateService(store, backup).SaveAsync(edited, "legacy-gitee");

        Assert.True(result.Success);
        Assert.Equal("Gitee Work", store.Config.FindService("gitee.com")?.DisplayName);
        Assert.Single(store.Config.GitServices, item => !item.IsBuiltIn);
        Assert.Equal(1, backup.SnapshotCount);
    }

    [Theory]
    [InlineData("github.com", "renamed")]
    [InlineData("legacy-gitee", "github.com")]
    [InlineData("legacy-gitee", "GITHUB.COM")]
    [InlineData("legacy-gitee", "bad id")]
    [InlineData("legacy-gitee", "")]
    [InlineData("missing", "gitee.com")]
    public async Task InvalidRenameLeavesOriginalConfigurationUntouched(string id, string newId)
    {
        var store = new InMemoryAppConfigStore { Config = CreateLegacyConfig() };
        var before = JsonSerializer.Serialize(store.Config);
        var backup = new NoOpBackupService();

        var result = await CreateService(store, backup).RenameAsync(id, newId);

        Assert.False(result.Success);
        Assert.Equal(before, JsonSerializer.Serialize(store.Config));
        Assert.Equal(0, backup.SnapshotCount);
    }

    [Fact]
    public async Task InvalidEditDuringRenameDoesNotChangeReferences()
    {
        var store = new InMemoryAppConfigStore { Config = CreateLegacyConfig() };
        var before = JsonSerializer.Serialize(store.Config);
        var edited = GitServiceService.CreateTemplate("Gitee");
        edited.SshPort = -1;
        var backup = new NoOpBackupService();

        Assert.False((await CreateService(store, backup).SaveAsync(edited, "legacy-gitee")).Success);
        Assert.Equal(before, JsonSerializer.Serialize(store.Config));
        Assert.Equal(0, backup.SnapshotCount);
    }

    [Fact]
    public async Task NewGenericTemplateDerivesReadableIdFromHost()
    {
        var template = GitServiceService.CreateTemplate("通用 Git 服务");
        Assert.Empty(template.Id);
        template.HostName = "git.example.com";
        template.WebBaseUrl = "https://git.example.com";
        var store = new InMemoryAppConfigStore();

        var result = await CreateService(store, new NoOpBackupService()).SaveAsync(template);

        Assert.True(result.Success);
        Assert.Equal("git.example.com", result.Value?.Id);
    }

    private static AppConfig CreateLegacyConfig()
    {
        var gitee = GitServiceService.CreateTemplate("Gitee");
        gitee.Id = "legacy-gitee";
        gitee.DefaultIdentityId = "work";
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), gitee],
            Identities = [new GitIdentity
            {
                Id = "work", ServiceInstanceId = gitee.Id, DisplayName = "Work", AccountName = "demo",
                HostAlias = "gitee-work", PrivateKeyPath = "C:/fake/keys/gitee", PublicKeyPath = "C:/fake/keys/gitee.pub"
            }],
            RepositoryRoutes = [new RepositoryRoute
            {
                Id = "team-route", ServiceInstanceId = gitee.Id, NamespacePath = "team", IdentityId = "work"
            }],
            GitProfiles = [new GitProfile
            {
                Id = "profile", DisplayName = "Profile", UserName = "Demo", UserEmail = "demo@example.com",
                DefaultServiceInstanceId = gitee.Id, DefaultIdentityId = "work"
            }],
            GitProfileRules = [new GitProfileRule { Id = "rule", ProfileId = "profile", Pattern = "https://gitee.com/**", Kind = GitProfileRuleKind.RemoteUrl }]
        };
        config.Normalize();
        return config;
    }

    private static GitServiceService CreateService(InMemoryAppConfigStore store, NoOpBackupService backup)
        => new(store, backup,
            new StubProcessRunner(_ => throw new InvalidOperationException("No network or SSH expected.")),
            new FixedToolchainService("git.exe"), GitProviderAdapterRegistry.CreateDefault());
}
