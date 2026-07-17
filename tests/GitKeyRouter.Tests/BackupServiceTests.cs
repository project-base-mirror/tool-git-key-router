using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.Backup;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task SnapshotAndRestore_PreservesAllThreeConfigurationTypes()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var fileSystem = new PhysicalFileSystem();
        var git = new FakeGitUrlRewriteStore();
        var originalRule = new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/");
        git.Rules.Add(originalRule);
        Directory.CreateDirectory(paths.AppDataDirectory);
        Directory.CreateDirectory(paths.SshDirectory);
        await File.WriteAllTextAsync(paths.ConfigPath, "{\"SchemaVersion\":1}");
        await File.WriteAllTextAsync(paths.SshConfigPath, "# original ssh config");
        var service = new BackupService(paths, fileSystem, git, new TestClock());

        var manifest = await service.CreateSnapshotAsync("test snapshot");
        await File.WriteAllTextAsync(paths.ConfigPath, "{\"changed\":true}");
        await File.WriteAllTextAsync(paths.SshConfigPath, "changed ssh config");
        git.Rules.Clear();
        git.Rules.Add(new GitUrlRewriteRule("git@wrong:", "https://github.com/"));

        Assert.True((await service.RestoreAppConfigAsync(manifest.BackupDirectory)).Success);
        Assert.True((await service.RestoreSshConfigAsync(manifest.BackupDirectory)).Success);
        Assert.True((await service.RestoreGitRewritesAsync(manifest.BackupDirectory)).Success);
        Assert.Equal("{\"SchemaVersion\":1}", await File.ReadAllTextAsync(paths.ConfigPath));
        Assert.Equal("# original ssh config", await File.ReadAllTextAsync(paths.SshConfigPath));
        Assert.Contains(originalRule, git.Rules);
        Assert.DoesNotContain(git.Rules, item => item.BaseUrl == "git@wrong:");
    }
}
