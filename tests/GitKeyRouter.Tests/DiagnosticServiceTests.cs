using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class DiagnosticServiceTests
{
    private const string OpenSsh = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4f first@example.com";

    [Fact]
    public async Task ReportsSharedPrivatePathAndCopiedPublicKeyMaterial()
    {
        using var directory = new TemporaryDirectory();
        var paths = new TestAppPaths(directory.Path);
        Directory.CreateDirectory(paths.SshDirectory);
        var sharedPrivatePath = Path.Combine(paths.SshDirectory, "id_shared");
        var firstPublicPath = Path.Combine(paths.SshDirectory, "id_first.pub");
        var secondPublicPath = Path.Combine(paths.SshDirectory, "id_second.pub");
        await File.WriteAllTextAsync(sharedPrivatePath, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret");
        await File.WriteAllTextAsync(firstPublicPath, OpenSsh);
        await File.WriteAllTextAsync(secondPublicPath, OpenSsh.Replace("first@example.com", "second@example.com", StringComparison.Ordinal));
        var configStore = new InMemoryAppConfigStore
        {
            Config = new AppConfig
            {
                Identities =
                [
                    Identity("one", "Account One", "github-one", sharedPrivatePath, firstPublicPath),
                    Identity("two", "Account Two", "github-two", sharedPrivatePath, secondPublicPath)
                ]
            }
        };
        var fileSystem = new PhysicalFileSystem();
        var backupService = new NoOpBackupService();
        var sshConfigService = new SshConfigService(fileSystem, paths, backupService);
        var gitRewriteService = new GitUrlRewriteService(configStore, new FakeGitUrlRewriteStore(), backupService);
        var service = new DiagnosticService(
            configStore,
            paths,
            fileSystem,
            new FixedToolchainService("git.exe", "ssh-keygen.exe"),
            sshConfigService,
            gitRewriteService,
            new TestClock());

        var report = await service.RunAsync();

        Assert.Contains(report.Items, item => item.Code == "PRIVATE_KEY_PATH_SHARED");
        Assert.Contains(report.Items, item => item.Code == "PUBLIC_KEY_MATERIAL_REUSED");
        var copiedKeyWarning = Assert.Single(report.Items, item => item.Code == "PUBLIC_KEY_MATERIAL_REUSED");
        Assert.DoesNotContain("AAAAC3", copiedKeyWarning.Message, StringComparison.Ordinal);
    }

    private static GitHubIdentity Identity(
        string id,
        string displayName,
        string hostAlias,
        string privateKeyPath,
        string publicKeyPath)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            GitHubUsername = id,
            HostAlias = hostAlias,
            PrivateKeyPath = privateKeyPath,
            PublicKeyPath = publicKeyPath,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
