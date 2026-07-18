using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task SaveAsync_RejectsAliasAlreadyDeclaredInUnmanagedSshConfig()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(paths.SshDirectory);
        await File.WriteAllTextAsync(
            paths.SshConfigPath,
            "Host work-git\n    HostName git.example\n");
        var configStore = new InMemoryAppConfigStore();
        var backup = new NoOpBackupService();
        var sshConfig = new SshConfigService(
            new PhysicalFileSystem(),
            paths,
            backup,
            GitProviderAdapterRegistry.CreateDefault());
        var service = new IdentityService(configStore, backup, new TestClock(), sshConfig);
        var identity = new GitIdentity
        {
            ServiceInstanceId = GitServiceInstance.GitHubComId,
            DisplayName = "Work",
            AccountName = "camus",
            HostAlias = "work-git",
            PrivateKeyPath = Path.Combine(paths.SshDirectory, "id_work"),
            PublicKeyPath = Path.Combine(paths.SshDirectory, "id_work.pub")
        };

        var result = await service.SaveAsync(identity);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("unmanaged SSH Host", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(configStore.Config.Identities);
        Assert.Equal(0, backup.SnapshotCount);
    }
}
