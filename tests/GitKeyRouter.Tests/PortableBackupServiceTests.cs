using System.Text;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.Backup;
using GitKeyRouter.Infrastructure.Configuration;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class PortableBackupServiceTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task ExportInspectImport_RoundTripsSettingsAndKeysAcrossDifferentComputerRoots()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "transfer.gkrbackup");
        var source = await CreateSourceAsync(Path.Combine(temp.Path, "source"), packagePath);
        var targetPaths = new TestAppPaths(Path.Combine(temp.Path, "target"));
        var targetFileSystem = new PhysicalFileSystem();
        var targetStore = new JsonAppConfigStore(targetPaths, targetFileSystem);
        var oldConfig = new AppConfig { UiLanguage = "en-US" };
        await targetStore.SaveAsync(oldConfig);
        Directory.CreateDirectory(targetPaths.SshDirectory);
        await File.WriteAllTextAsync(targetPaths.SshConfigPath, "# target before import");
        var targetGit = new FakeGitUrlRewriteStore();
        targetGit.Rules.Add(new GitUrlRewriteRule("git@old:", "https://old.example/"));
        var profiles = new RecordingProfileMaterializer();
        var targetService = CreateService(targetPaths, targetFileSystem, targetStore, targetGit, profiles);

        var preview = await targetService.InspectAsync(packagePath, Password);
        var result = await targetService.ImportAsync(packagePath, Password);

        Assert.True(preview.Success, string.Join(Environment.NewLine, preview.Errors));
        Assert.Equal(1, preview.Value!.IdentityCount);
        Assert.Equal(2, preview.Value.KeyFileCount);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(1, profiles.ApplyCount);

        var imported = await targetStore.LoadAsync();
        var identity = Assert.Single(imported.Identities);
        var managedRoot = Path.GetFullPath(Path.Combine(targetPaths.SshDirectory, "GitKeyRouter"));
        Assert.StartsWith(
            managedRoot + Path.DirectorySeparatorChar,
            Path.GetFullPath(identity.PrivateKeyPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            managedRoot + Path.DirectorySeparatorChar,
            Path.GetFullPath(identity.PublicKeyPath),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source.PrivateKey, await File.ReadAllBytesAsync(identity.PrivateKeyPath));
        Assert.Equal(source.PublicKey, await File.ReadAllBytesAsync(identity.PublicKeyPath));

        var ssh = await File.ReadAllTextAsync(targetPaths.SshConfigPath);
        Assert.DoesNotContain(source.PrivateKeyPath, ssh, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(identity.PrivateKeyPath.Replace('\\', '/'), ssh, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(source.Rule, Assert.Single(targetGit.Rules));
        var encryptedBytes = await File.ReadAllBytesAsync(packagePath);
        Assert.DoesNotContain(
            "PRIVATE-KEY-MATERIAL",
            Encoding.UTF8.GetString(encryptedBytes),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPasswordAndTamperedPackage_DoNotChangeTargetState()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "transfer.gkrbackup");
        _ = await CreateSourceAsync(Path.Combine(temp.Path, "source"), packagePath);
        var targetPaths = new TestAppPaths(Path.Combine(temp.Path, "target"));
        var fileSystem = new PhysicalFileSystem();
        var store = new JsonAppConfigStore(targetPaths, fileSystem);
        await store.SaveAsync(new AppConfig { UiLanguage = "en-US" });
        Directory.CreateDirectory(targetPaths.SshDirectory);
        await File.WriteAllTextAsync(targetPaths.SshConfigPath, "# untouched");
        var git = new FakeGitUrlRewriteStore();
        var originalRule = new GitUrlRewriteRule("git@old:", "https://old.example/");
        git.Rules.Add(originalRule);
        var service = CreateService(targetPaths, fileSystem, store, git, new RecordingProfileMaterializer());
        var configBefore = await File.ReadAllBytesAsync(targetPaths.ConfigPath);
        var sshBefore = await File.ReadAllBytesAsync(targetPaths.SshConfigPath);

        var wrong = await service.ImportAsync(packagePath, "wrong password");
        var tamperedPath = Path.Combine(temp.Path, "tampered.gkrbackup");
        var tampered = await File.ReadAllBytesAsync(packagePath);
        tampered[^1] ^= 0x5A;
        await File.WriteAllBytesAsync(tamperedPath, tampered);
        var changed = await service.ImportAsync(tamperedPath, Password);

        Assert.False(wrong.Success);
        Assert.False(changed.Success);
        Assert.Equal(configBefore, await File.ReadAllBytesAsync(targetPaths.ConfigPath));
        Assert.Equal(sshBefore, await File.ReadAllBytesAsync(targetPaths.SshConfigPath));
        Assert.Equal(originalRule, Assert.Single(git.Rules));
        Assert.False(Directory.Exists(Path.Combine(targetPaths.SshDirectory, "GitKeyRouter")));
    }

    [Fact]
    public async Task ProfileMaterializationFailure_RollsBackAllImportedState()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "transfer.gkrbackup");
        _ = await CreateSourceAsync(Path.Combine(temp.Path, "source"), packagePath);
        var targetPaths = new TestAppPaths(Path.Combine(temp.Path, "target"));
        var fileSystem = new PhysicalFileSystem();
        var store = new JsonAppConfigStore(targetPaths, fileSystem);
        await store.SaveAsync(new AppConfig { UiLanguage = "en-US" });
        Directory.CreateDirectory(targetPaths.SshDirectory);
        await File.WriteAllTextAsync(targetPaths.SshConfigPath, "# original target ssh");
        var git = new FakeGitUrlRewriteStore();
        var originalRule = new GitUrlRewriteRule("git@original:", "https://original.example/");
        git.Rules.Add(originalRule);
        var profiles = new RecordingProfileMaterializer { FailFirstApply = true };
        var service = CreateService(targetPaths, fileSystem, store, git, profiles);
        var configBefore = await File.ReadAllBytesAsync(targetPaths.ConfigPath);
        var sshBefore = await File.ReadAllBytesAsync(targetPaths.SshConfigPath);

        var result = await service.ImportAsync(packagePath, Password);

        Assert.False(result.Success);
        Assert.Contains("restored automatically", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, profiles.ApplyCount);
        Assert.Equal(configBefore, await File.ReadAllBytesAsync(targetPaths.ConfigPath));
        Assert.Equal(sshBefore, await File.ReadAllBytesAsync(targetPaths.SshConfigPath));
        Assert.Equal(originalRule, Assert.Single(git.Rules));
        var managedRoot = Path.Combine(targetPaths.SshDirectory, "GitKeyRouter");
        Assert.Empty(Directory.Exists(managedRoot)
            ? Directory.EnumerateFiles(managedRoot, "*", SearchOption.AllDirectories)
            : Enumerable.Empty<string>());
    }

    [Fact]
    public async Task Import_RemovesDistinctCaseSensitiveRewriteKeys()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "transfer.gkrbackup");
        var source = await CreateSourceAsync(Path.Combine(temp.Path, "source"), packagePath);
        var paths = new TestAppPaths(Path.Combine(temp.Path, "target"));
        var fileSystem = new PhysicalFileSystem();
        var store = new JsonAppConfigStore(paths, fileSystem);
        var git = new FakeGitUrlRewriteStore();
        git.Rules.Add(new GitUrlRewriteRule("git@host:Team/", "https://host/Team/"));
        git.Rules.Add(new GitUrlRewriteRule("git@host:team/", "https://host/team/"));
        var service = CreateService(paths, fileSystem, store, git, new RecordingProfileMaterializer());

        var result = await service.ImportAsync(packagePath, Password);

        Assert.True(result.Success, string.Join(Environment.NewLine, [result.Message, .. result.Errors]));
        Assert.Equal(source.Rule, Assert.Single(git.Rules));
    }

    [Fact]
    public async Task Import_RemapChangesOnlyExactKeyPathArgumentsAndQuotesTargetPaths()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "transfer.gkrbackup");
        var source = await CreateSourceAsync(
            Path.Combine(temp.Path, "source"),
            packagePath,
            privatePath =>
                $"Host git-work\r\n  IdentityFile \"{privatePath}\" # keep {privatePath}\r\n"
                + $"  CertificateFile = '{privatePath}.pub'\r\n"
                + $"  identityfile={privatePath.Replace('\\', '/')}\r\n"
                + $"Host unrelated\r\n  IdentityFile \"{privatePath}_personal\"\r\n"
                + $"  ProxyCommand echo \"{privatePath}\"\r\n"
                + $"# IdentityFile \"{privatePath}\"\r\n");
        var paths = new TestAppPaths(Path.Combine(temp.Path, "target with spaces"));
        var fileSystem = new PhysicalFileSystem();
        var store = new JsonAppConfigStore(paths, fileSystem);
        var git = new FakeGitUrlRewriteStore();
        var service = CreateService(paths, fileSystem, store, git, new RecordingProfileMaterializer());

        var result = await service.ImportAsync(packagePath, Password);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        var identity = Assert.Single((await store.LoadAsync()).Identities);
        var privateTarget = identity.PrivateKeyPath.Replace('\\', '/');
        var publicTarget = identity.PublicKeyPath.Replace('\\', '/');
        var expected = $"Host git-work\r\n  IdentityFile \"{privateTarget}\" # keep {source.PrivateKeyPath}\r\n"
            + $"  CertificateFile = \"{publicTarget}\"\r\n"
            + $"  identityfile=\"{privateTarget}\"\r\n"
            + $"Host unrelated\r\n  IdentityFile \"{source.PrivateKeyPath}_personal\"\r\n"
            + $"  ProxyCommand echo \"{source.PrivateKeyPath}\"\r\n"
            + $"# IdentityFile \"{source.PrivateKeyPath}\"\r\n";
        Assert.Equal(expected, await File.ReadAllTextAsync(paths.SshConfigPath));
    }

    private static PortableBackupService CreateService(
        TestAppPaths paths,
        IFileSystem fileSystem,
        IAppConfigStore configStore,
        FakeGitUrlRewriteStore git,
        IGitProfileMaterializer profiles)
    {
        var backup = new BackupService(paths, fileSystem, git, new TestClock());
        return new PortableBackupService(
            paths,
            fileSystem,
            configStore,
            git,
            backup,
            profiles,
            new TestClock());
    }

    private static async Task<SourceFixture> CreateSourceAsync(
        string root,
        string packagePath,
        Func<string, string>? sshConfigFactory = null)
    {
        var paths = new TestAppPaths(root);
        var fileSystem = new PhysicalFileSystem();
        var store = new JsonAppConfigStore(paths, fileSystem);
        Directory.CreateDirectory(paths.SshDirectory);
        var privatePath = Path.Combine(paths.SshDirectory, "id_work");
        var publicPath = privatePath + ".pub";
        var privateKey = Encoding.UTF8.GetBytes("PRIVATE-KEY-MATERIAL");
        var publicKey = Encoding.UTF8.GetBytes("ssh-ed25519 PUBLIC-KEY-MATERIAL work@example");
        await File.WriteAllBytesAsync(privatePath, privateKey);
        await File.WriteAllBytesAsync(publicPath, publicKey);
        await store.SaveAsync(new AppConfig
        {
            UiLanguage = "zh-CN",
            Identities =
            [
                new GitIdentity
                {
                    Id = "work",
                    DisplayName = "Work",
                    AccountName = "camus",
                    HostAlias = "git-work",
                    PrivateKeyPath = privatePath,
                    PublicKeyPath = publicPath,
                    EmailOrComment = "work@example"
                }
            ]
        });
        await File.WriteAllTextAsync(
            paths.SshConfigPath,
            sshConfigFactory?.Invoke(privatePath)
                ?? $"Host git-work{Environment.NewLine}  IdentityFile \"{privatePath}\"{Environment.NewLine}");
        var git = new FakeGitUrlRewriteStore();
        var rule = new GitUrlRewriteRule("git@git-work:", "https://github.com/");
        git.Rules.Add(rule);
        var service = CreateService(paths, fileSystem, store, git, new RecordingProfileMaterializer());

        var result = await service.ExportAsync(packagePath, Password);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        return new SourceFixture(privatePath, privateKey, publicKey, rule);
    }

    private sealed record SourceFixture(
        string PrivateKeyPath,
        byte[] PrivateKey,
        byte[] PublicKey,
        GitUrlRewriteRule Rule);

    private sealed class RecordingProfileMaterializer : IGitProfileMaterializer
    {
        public int ApplyCount { get; private set; }

        public bool FailFirstApply { get; init; }

        public Task<OperationResult> ApplyCurrentAsync(CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return Task.FromResult(
                FailFirstApply && ApplyCount == 1
                    ? OperationResult.Fail("Simulated Git Profile apply failure.")
                    : OperationResult.Ok("Git Profile files applied."));
        }
    }
}
