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
        const string originalConfig = "{\"SchemaVersion\":3,\"GitServices\":[],\"Identities\":[],\"RepositoryRoutes\":[],\"GitProfiles\":[],\"GitProfileRules\":[]}";
        await File.WriteAllTextAsync(paths.ConfigPath, originalConfig);
        await File.WriteAllTextAsync(paths.SshConfigPath, "# original ssh config");
        var service = new BackupService(paths, fileSystem, git, new TestClock());

        var manifest = await service.CreateSnapshotAsync("test snapshot");
        Assert.Equal(3, manifest.AppConfigSchemaVersion);
        await File.WriteAllTextAsync(paths.ConfigPath, "{\"changed\":true}");
        await File.WriteAllTextAsync(paths.SshConfigPath, "changed ssh config");
        git.Rules.Clear();
        git.Rules.Add(new GitUrlRewriteRule("git@wrong:", "https://github.com/"));

        Assert.True((await service.RestoreAppConfigAsync(manifest.BackupDirectory)).Success);
        Assert.True((await service.RestoreSshConfigAsync(manifest.BackupDirectory)).Success);
        Assert.True((await service.RestoreGitRewritesAsync(manifest.BackupDirectory)).Success);
        Assert.Equal(originalConfig, await File.ReadAllTextAsync(paths.ConfigPath));
        Assert.Equal("# original ssh config", await File.ReadAllTextAsync(paths.SshConfigPath));
        Assert.Contains(originalRule, git.Rules);
        Assert.DoesNotContain(git.Rules, item => item.BaseUrl == "git@wrong:");
    }

    [Fact]
    public async Task RestoreAppConfig_RejectsFutureSchemaWithoutChangingCurrentConfiguration()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var fileSystem = new PhysicalFileSystem();
        Directory.CreateDirectory(paths.AppDataDirectory);
        Directory.CreateDirectory(paths.BackupRootDirectory);
        const string currentConfig = "{\"SchemaVersion\":3,\"GitServices\":[],\"Identities\":[],\"RepositoryRoutes\":[],\"GitProfiles\":[],\"GitProfileRules\":[]}";
        await File.WriteAllTextAsync(paths.ConfigPath, currentConfig);
        var backupDirectory = Path.Combine(paths.BackupRootDirectory, "future");
        Directory.CreateDirectory(backupDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(backupDirectory, "manifest.json"),
            "{\"SchemaVersion\":1,\"BackupDirectory\":\"future\",\"CreatedAt\":\"2026-07-18T00:00:00Z\",\"Reason\":\"future\",\"AppConfigExisted\":true,\"AppConfigSchemaVersion\":99,\"SshConfigExisted\":false,\"GitRewriteCaptureSucceeded\":true,\"GitRewriteCount\":0}");
        await File.WriteAllTextAsync(Path.Combine(backupDirectory, "app_config.json"), "{\"SchemaVersion\":99}");
        await File.WriteAllTextAsync(Path.Combine(backupDirectory, "git_url_rewrites.json"), "[]");
        var service = new BackupService(paths, fileSystem, new FakeGitUrlRewriteStore(), new TestClock());

        var result = await service.RestoreAppConfigAsync(backupDirectory);

        Assert.False(result.Success);
        Assert.Contains("supports up to schema", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(currentConfig, await File.ReadAllTextAsync(paths.ConfigPath));
    }
}
