using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class SshKeyServiceTests
{
    private const string OpenSsh = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIAABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4f test@example.com";
    private const string Rfc4716 = "---- BEGIN SSH2 PUBLIC KEY ----\nAQID\n---- END SSH2 PUBLIC KEY ----";
    private const string Pem = "-----BEGIN PUBLIC KEY-----\nAQID\n-----END PUBLIC KEY-----";

    [Fact]
    public async Task ListsAllPublicKeyVariantsAndSkipsPrivateMaterial()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        await File.WriteAllTextAsync(identity.PublicKeyPath, OpenSsh);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "id_demo.rfc4716.pub"), Rfc4716);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "id_demo.pem.pub"), Pem);
        await File.WriteAllTextAsync(identity.PrivateKeyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "id_demo.pem.pub.gitkeyrouter.20260717-120000.bak"), Pem);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "id_demo.tmp"), Pem);
        var service = CreateService(_ => Success());

        var result = await service.ListPublicKeyVariantsAsync(identity);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Value.Count);
        Assert.Contains(result.Value, item => item.Inspection.Format == SshKeyFormat.OpenSshPublic && item.IsConfiguredPath);
        Assert.Contains(result.Value, item => item.Inspection.Format == SshKeyFormat.Rfc4716Public);
        Assert.Contains(result.Value, item => item.Inspection.Format == SshKeyFormat.PemPublic);
        Assert.DoesNotContain(result.Value, item => item.Inspection.IsPrivateMaterial);
    }

    [Fact]
    public async Task CopyForGitHubRejectsNonOpenSshVariant()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "id_demo.pem.pub");
        await File.WriteAllTextAsync(path, Pem);
        var service = CreateService(_ => Success());

        var result = await service.ReadPublicKeyAsync(path, requireOpenSsh: true);

        Assert.False(result.Success);
        Assert.Contains("not an OpenSSH public key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConvertsRfc4716ToSeparateOpenSshFile()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        var source = Path.Combine(directory.Path, "id_demo.rfc4716.pub");
        await File.WriteAllTextAsync(source, Rfc4716);
        var runner = new StubProcessRunner(request =>
            request.Arguments.Contains("-i") ? Success(OpenSsh) : Success());
        var service = CreateService(runner);

        var result = await service.ConvertPublicKeyAsync(
            identity,
            source,
            SshPublicKeyExportFormat.OpenSsh,
            overwrite: false);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(Path.Combine(directory.Path, "id_demo.openssh.pub"), result.Value.DestinationPath);
        Assert.Equal(OpenSsh, (await File.ReadAllTextAsync(result.Value.DestinationPath)).Trim());
        Assert.Equal(Rfc4716, await File.ReadAllTextAsync(source));
        Assert.Single(runner.Requests);
        Assert.Equal(["-i", "-m", "RFC4716", "-f", source], runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task ConvertsOpenSshToSeparateRfc4716File()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        await File.WriteAllTextAsync(identity.PublicKeyPath, OpenSsh);
        var runner = new StubProcessRunner(request =>
            request.Arguments.Contains("-e") ? Success(Rfc4716) : Success());
        var service = CreateService(runner);

        var result = await service.ConvertPublicKeyAsync(
            identity,
            identity.PublicKeyPath,
            SshPublicKeyExportFormat.Rfc4716,
            overwrite: false);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(Path.Combine(directory.Path, "id_demo.rfc4716.pub"), result.Value.DestinationPath);
        Assert.Equal(Rfc4716, (await File.ReadAllTextAsync(result.Value.DestinationPath)).Trim());
        Assert.Equal(OpenSsh, await File.ReadAllTextAsync(identity.PublicKeyPath));
        Assert.Single(runner.Requests);
        Assert.Equal("RFC4716", runner.Requests[0].Arguments[2]);
    }

    [Fact]
    public async Task OverwriteCreatesBackupAndPreservesSource()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        await File.WriteAllTextAsync(identity.PublicKeyPath, OpenSsh);
        var destination = Path.Combine(directory.Path, "id_demo.pem.pub");
        await File.WriteAllTextAsync(destination, "old pem");
        var runner = new StubProcessRunner(_ => Success(Pem));
        var service = CreateService(runner);

        var result = await service.ConvertPublicKeyAsync(
            identity,
            identity.PublicKeyPath,
            SshPublicKeyExportFormat.Pem,
            overwrite: true);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.BackupFile);
        Assert.True(File.Exists(result.Value.BackupFile));
        Assert.Equal("old pem", await File.ReadAllTextAsync(result.Value.BackupFile));
        Assert.Equal(Pem, (await File.ReadAllTextAsync(destination)).Trim());
        Assert.Equal(OpenSsh, await File.ReadAllTextAsync(identity.PublicKeyPath));
    }

    [Fact]
    public async Task DerivesOpenSshPublicVariantFromConfiguredPrivateKey()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        await File.WriteAllTextAsync(identity.PrivateKeyPath, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret");
        var runner = new StubProcessRunner(request =>
            request.Arguments.Contains("-y") ? Success(OpenSsh) : Success());
        var service = CreateService(runner);

        var result = await service.ConvertPublicKeyAsync(
            identity,
            identity.PrivateKeyPath,
            SshPublicKeyExportFormat.OpenSsh,
            overwrite: false);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(Path.Combine(directory.Path, "id_demo.openssh.pub"), result.Value.DestinationPath);
        Assert.Equal(OpenSsh, (await File.ReadAllTextAsync(result.Value.DestinationPath)).Trim());
        Assert.Equal("-----BEGIN OPENSSH PRIVATE KEY-----\nsecret", await File.ReadAllTextAsync(identity.PrivateKeyPath));
        Assert.Single(runner.Requests);
        Assert.Contains("-y", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task PuttyPrivateKeyRequiresPuttygen()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        await File.WriteAllTextAsync(identity.PrivateKeyPath, "PuTTY-User-Key-File-3: ssh-rsa");
        var service = CreateService(_ => Success(OpenSsh));

        var result = await service.ConvertPublicKeyAsync(
            identity,
            identity.PrivateKeyPath,
            SshPublicKeyExportFormat.OpenSsh,
            overwrite: false);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("PuTTYgen", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(directory.Path, "id_demo.openssh.pub")));
    }

    [Fact]
    public async Task RefusesPrivateMaterialOutsideConfiguredPrivatePath()
    {
        using var directory = new TemporaryDirectory();
        var identity = CreateIdentity(directory.Path);
        var misplaced = Path.Combine(directory.Path, "misplaced.pub");
        await File.WriteAllTextAsync(misplaced, "-----BEGIN OPENSSH PRIVATE KEY-----\nsecret");
        var service = CreateService(_ => Success(OpenSsh));

        var result = await service.ConvertPublicKeyAsync(
            identity,
            misplaced,
            SshPublicKeyExportFormat.OpenSsh,
            overwrite: false);

        Assert.False(result.Success);
        Assert.Contains("outside the configured private-key path", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(directory.Path, "id_demo.openssh.pub")));
    }

    private static GitHubIdentity CreateIdentity(string directory)
        => new()
        {
            DisplayName = "Demo",
            GitHubUsername = "demo",
            HostAlias = "github-demo",
            PrivateKeyPath = Path.Combine(directory, "id_demo"),
            PublicKeyPath = Path.Combine(directory, "id_demo.pub")
        };

    private static SshKeyService CreateService(Func<ProcessRequest, ProcessResult> handler)
        => CreateService(new StubProcessRunner(handler));

    private static SshKeyService CreateService(StubProcessRunner runner)
        => new(
            new PhysicalFileSystem(),
            runner,
            new FixedToolchainService("git.exe", "ssh-keygen.exe"),
            new TestClock());

    private static ProcessResult Success(string stdout = "")
        => new()
        {
            ExecutablePath = "ssh-keygen.exe",
            ExitCode = 0,
            StandardOutput = stdout
        };
}
