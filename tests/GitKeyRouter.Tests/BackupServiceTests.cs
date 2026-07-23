using System.Security.Cryptography;
using System.Text.Json;
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
        Assert.Equal(2, manifest.SchemaVersion);
        Assert.Equal(3, manifest.AppConfigSchemaVersion);
        Assert.Equal(3, manifest.Files.Count);
        foreach (var (fileName, integrity) in manifest.Files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(manifest.BackupDirectory, fileName));
            Assert.Equal(bytes.LongLength, integrity.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), integrity.Sha256);
        }

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

    [Fact]
    public async Task RestoreAppConfig_RejectsTamperedBackupWithoutChangingCurrentConfiguration()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var fileSystem = new PhysicalFileSystem();
        Directory.CreateDirectory(paths.AppDataDirectory);
        const string originalConfig = "{\"SchemaVersion\":3,\"GitServices\":[],\"Identities\":[],\"RepositoryRoutes\":[],\"GitProfiles\":[],\"GitProfileRules\":[]}";
        await File.WriteAllTextAsync(paths.ConfigPath, originalConfig);
        var service = new BackupService(paths, fileSystem, new FakeGitUrlRewriteStore(), new TestClock());
        var manifest = await service.CreateSnapshotAsync("tamper test");
        await File.AppendAllTextAsync(Path.Combine(manifest.BackupDirectory, "app_config.json"), "tampered");
        const string currentConfig = "{\"SchemaVersion\":3,\"changed\":true}";
        await File.WriteAllTextAsync(paths.ConfigPath, currentConfig);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(manifest.BackupDirectory));
        var result = await service.RestoreAppConfigAsync(manifest.BackupDirectory);

        Assert.False(result.Success);
        Assert.Contains("integrity", string.Join(' ', result.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(currentConfig, await File.ReadAllTextAsync(paths.ConfigPath));
    }

    [Fact]
    public async Task ReadAsync_RemainsCompatibleWithLegacySchemaOneManifest()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var fileSystem = new PhysicalFileSystem();
        Directory.CreateDirectory(paths.AppDataDirectory);
        await File.WriteAllTextAsync(paths.ConfigPath, "{\"SchemaVersion\":3}");
        var service = new BackupService(paths, fileSystem, new FakeGitUrlRewriteStore(), new TestClock());
        var manifest = await service.CreateSnapshotAsync("legacy test");
        manifest.SchemaVersion = 1;
        manifest.Files.Clear();
        await File.WriteAllTextAsync(
            Path.Combine(manifest.BackupDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest));
        await File.AppendAllTextAsync(Path.Combine(manifest.BackupDirectory, "app_config.json"), " ");

        var snapshot = await service.ReadAsync(manifest.BackupDirectory);

        Assert.Equal(1, snapshot.Manifest.SchemaVersion);
        Assert.NotNull(snapshot.AppConfigText);
    }

    [Fact]
    public async Task RestoreGitRewrites_RollsBackAutomaticallyWhenApplyFails()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var fileSystem = new PhysicalFileSystem();
        var git = new FakeGitUrlRewriteStore();
        var targetRule = new GitUrlRewriteRule("git@target:", "https://target.example/");
        var originalRule = new GitUrlRewriteRule("git@original:", "https://original.example/");
        git.Rules.Add(targetRule);
        var service = new BackupService(paths, fileSystem, git, new TestClock());
        var selected = await service.CreateSnapshotAsync("selected target");
        git.Rules.Clear();
        git.Rules.Add(originalRule);
        git.FailNextAdd = true;

        var result = await service.RestoreGitRewritesAsync(selected.BackupDirectory);

        Assert.False(result.Success);
        Assert.Contains("restored automatically", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([originalRule], git.Rules);
    }
}
