using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GitServiceServiceTests
{
    [Fact]
    public async Task BuiltInGitHubServiceCannotBeDeleted()
    {
        var store = new InMemoryAppConfigStore();
        var backup = new NoOpBackupService();
        var service = CreateService(store, backup);

        var result = await service.DeleteAsync(GitServiceInstance.GitHubComId);

        Assert.False(result.Success);
        Assert.Contains("cannot be deleted", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, backup.SnapshotCount);
        Assert.NotNull(store.Config.FindService(GitServiceInstance.GitHubComId));
    }

    [Fact]
    public async Task SavesAndDeletesUnreferencedGiteaService()
    {
        var store = new InMemoryAppConfigStore();
        var backup = new NoOpBackupService();
        var service = CreateService(store, backup);
        var gitea = new GitServiceInstance
        {
            Id = string.Empty,
            DisplayName = "Home Gitea",
            ProviderKind = GitProviderKind.Gitea,
            HostName = "git.home.example",
            SshPort = 2222,
            SshUser = "git",
            WebBaseUrl = "https://git.home.example"
        };

        var saved = await service.SaveAsync(gitea);

        Assert.True(saved.Success);
        Assert.NotNull(saved.Value);
        Assert.Equal("git.home.example", saved.Value.Id);
        Assert.NotNull(store.Config.FindService(saved.Value.Id));
        Assert.Equal(1, backup.SnapshotCount);

        var deleted = await service.DeleteAsync(saved.Value.Id);

        Assert.True(deleted.Success);
        Assert.Null(store.Config.FindService(saved.Value.Id));
        Assert.Equal(2, backup.SnapshotCount);
    }

    [Fact]
    public async Task ReferencedServiceCannotBeDeleted()
    {
        var gitea = GitServiceService.CreateTemplate("自建 Gitea");
        gitea.Id = "gitea-office";
        gitea.HostName = "git.office.example";
        gitea.WebBaseUrl = "https://git.office.example";
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig
            {
                GitServices = [GitServiceInstance.CreateGitHubCom(), gitea],
                Identities =
                [
                    new GitIdentity
                    {
                        Id = "work",
                        ServiceInstanceId = gitea.Id,
                        DisplayName = "Work",
                        AccountName = "camus",
                        HostAlias = "gitea-work"
                    }
                ]
            }
        };
        var service = CreateService(store, new NoOpBackupService());

        var result = await service.DeleteAsync(gitea.Id);

        Assert.False(result.Success);
        Assert.Contains("referenced", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionTestUsesCustomPortAndRecognizesGitLabGreeting()
    {
        var runner = new StubProcessRunner(_ => new ProcessResult
        {
            ExecutablePath = "ssh.exe",
            ExitCode = 1,
            StandardError = "Welcome to GitLab, @camus!"
        });
        var service = new GitServiceService(
            new InMemoryAppConfigStore(),
            new NoOpBackupService(),
            runner,
            new FixedToolchainService("git.exe", sshPath: "ssh.exe"),
            GitProviderAdapterRegistry.CreateDefault());
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

        var result = await service.TestConnectionAsync(gitLab);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Authenticated);
        var request = Assert.Single(runner.Requests);
        Assert.Contains("-p", request.Arguments);
        Assert.Contains("2222", request.Arguments);
        Assert.Contains("git@gitlab.office.example", request.Arguments);
    }

    [Fact]
    public void GiteaAdapterBuildsCustomPortSshBlockAndNestedNamespaceRules()
    {
        var service = new GitServiceInstance
        {
            Id = "gitea",
            DisplayName = "Gitea",
            ProviderKind = GitProviderKind.Gitea,
            HostName = "gitea.example",
            SshPort = 2222,
            SshUser = "git",
            WebBaseUrl = "https://gitea.example"
        };
        var identity = new GitIdentity
        {
            ServiceInstanceId = service.Id,
            HostAlias = "gitea-camus",
            PrivateKeyPath = "C:\\keys\\gitea"
        };
        var route = new RepositoryRoute
        {
            ServiceInstanceId = service.Id,
            NamespacePath = "team/platform",
            IdentityId = identity.Id
        };
        var adapter = GitProviderAdapterRegistry.CreateDefault().Get(GitProviderKind.Gitea);

        var block = adapter.BuildSshManagedBlock(service, identity, "\n");
        var rules = adapter.BuildRewriteRules(service, identity, route);

        Assert.Contains("    Port 2222\n", block, StringComparison.Ordinal);
        Assert.Equal("git@gitea-camus:team/platform/", rules[0].BaseUrl);
        Assert.Equal("https://gitea.example/team/platform/", rules[0].InsteadOfUrl);
    }

    private static GitServiceService CreateService(InMemoryAppConfigStore store, NoOpBackupService backup)
        => new(
            store,
            backup,
            new StubProcessRunner(_ => new ProcessResult { ExecutablePath = "ssh.exe" }),
            new FixedToolchainService("git.exe"),
            GitProviderAdapterRegistry.CreateDefault());
}
