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

    [Fact]
    public async Task ReportsManagedSshBlockThatTargetsTheWrongServiceEndpoint()
    {
        using var directory = new TemporaryDirectory();
        var paths = new TestAppPaths(directory.Path);
        Directory.CreateDirectory(paths.SshDirectory);
        var privateKey = Path.Combine(paths.SshDirectory, "id_gitea");
        var publicKey = privateKey + ".pub";
        await File.WriteAllTextAsync(privateKey, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret");
        await File.WriteAllTextAsync(publicKey, OpenSsh);
        await File.WriteAllTextAsync(paths.SshConfigPath, """
        # BEGIN GitKeyRouter managed block: gitea-camus
        Host gitea-camus
            HostName github.com
            User git
            IdentityFile C:/wrong/key
            IdentitiesOnly yes
        # END GitKeyRouter managed block: gitea-camus
        """);
        var gitea = new GitServiceInstance
        {
            Id = "gitea-home",
            DisplayName = "Home Gitea",
            ProviderKind = GitProviderKind.Gitea,
            HostName = "git.home.example",
            SshPort = 2222,
            SshUser = "git",
            WebBaseUrl = "https://git.home.example"
        };
        var configStore = new InMemoryAppConfigStore
        {
            Config = new AppConfig
            {
                GitServices = [GitServiceInstance.CreateGitHubCom(), gitea],
                Identities =
                [
                    new GitIdentity
                    {
                        Id = "gitea-camus",
                        ServiceInstanceId = gitea.Id,
                        DisplayName = "Gitea Camus",
                        AccountName = "camus",
                        HostAlias = "gitea-camus",
                        PrivateKeyPath = privateKey,
                        PublicKeyPath = publicKey
                    }
                ]
            }
        };
        var fileSystem = new PhysicalFileSystem();
        var backup = new NoOpBackupService();
        var providers = GitProviderAdapterRegistry.CreateDefault();
        var sshConfig = new SshConfigService(fileSystem, paths, backup, providers);
        var rewrites = new GitUrlRewriteService(configStore, new FakeGitUrlRewriteStore(), backup, providers);
        var diagnostics = new DiagnosticService(
            configStore,
            paths,
            fileSystem,
            new FixedToolchainService("git.exe", "ssh-keygen.exe", "ssh.exe"),
            sshConfig,
            rewrites,
            new TestClock(),
            providers);

        var report = await diagnostics.RunAsync();

        var mismatch = Assert.Single(report.Items, item => item.Code == "SSH_BLOCK_SERVICE_MISMATCH");
        Assert.Contains("git.home.example:2222", mismatch.Message, StringComparison.OrdinalIgnoreCase);
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
