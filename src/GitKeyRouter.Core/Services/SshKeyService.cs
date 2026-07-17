using System.Text.RegularExpressions;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed partial class SshKeyService
{
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainService _toolchainService;
    private readonly IClock _clock;

    public SshKeyService(
        IFileSystem fileSystem,
        IProcessRunner processRunner,
        IToolchainService toolchainService,
        IClock clock)
    {
        _fileSystem = fileSystem;
        _processRunner = processRunner;
        _toolchainService = toolchainService;
        _clock = clock;
    }

    public static string CreateDefaultPrivateKeyPath(string sshDirectory, string hostAlias)
    {
        var validation = HostAliasValidator.Validate(hostAlias);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, validation.Errors), nameof(hostAlias));
        }

        var normalizedAlias = NonFileNameCharacterPattern().Replace(hostAlias, "_");
        return Path.Combine(sshDirectory, $"id_ed25519_{normalizedAlias}");
    }

    public bool PrivateKeyExists(GitHubIdentity identity) => _fileSystem.FileExists(identity.PrivateKeyPath);

    public bool PublicKeyExists(GitHubIdentity identity) => _fileSystem.FileExists(identity.PublicKeyPath);

    public async Task<OperationResult<string>> ReadPublicKeyAsync(GitHubIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(identity.PublicKeyPath))
        {
            return OperationResult<string>.Fail("Public key file does not exist.", identity.PublicKeyPath);
        }

        try
        {
            var text = await _fileSystem.ReadAllTextAsync(identity.PublicKeyPath, cancellationToken).ConfigureAwait(false);
            if (text.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<string>.Fail("The selected public key file appears to contain private key data. It will not be displayed.");
            }

            return OperationResult<string>.Ok(text.Trim(), "Public key loaded.");
        }
        catch (Exception exception)
        {
            return OperationResult<string>.Fail("Unable to read the public key.", exception.Message);
        }
    }

    public async Task<OperationResult<SshKeyGenerationResult>> GenerateAsync(
        GitHubIdentity identity,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.SshKeygen.Exists || string.IsNullOrWhiteSpace(tools.SshKeygen.SelectedPath))
        {
            return OperationResult<SshKeyGenerationResult>.Fail(
                "ssh-keygen.exe was not found.",
                "Enable the Windows OpenSSH Client or install Git for Windows. GitKeyRouter will not install it automatically.");
        }

        var existingFiles = new[] { identity.PrivateKeyPath, identity.PublicKeyPath }
            .Where(_fileSystem.FileExists)
            .ToList();
        if (existingFiles.Count > 0 && !overwrite)
        {
            return OperationResult<SshKeyGenerationResult>.Fail(
                "The target key file already exists. Choose another filename or explicitly confirm overwrite.",
                existingFiles.ToArray());
        }

        var keyDirectory = Path.GetDirectoryName(identity.PrivateKeyPath);
        if (string.IsNullOrWhiteSpace(keyDirectory))
        {
            return OperationResult<SshKeyGenerationResult>.Fail("The private key path has no valid parent directory.");
        }

        _fileSystem.CreateDirectory(keyDirectory);
        var backups = new List<string>();
        if (overwrite)
        {
            foreach (var existingFile in existingFiles)
            {
                var backupPath = $"{existingFile}.gitkeyrouter.{_clock.LocalNow:yyyyMMdd-HHmmss}.bak";
                _fileSystem.CopyFile(existingFile, backupPath, false);
                backups.Add(backupPath);
                _fileSystem.DeleteFile(existingFile);
            }
        }

        var comment = string.IsNullOrWhiteSpace(identity.EmailOrComment)
            ? identity.GitHubUsername
            : identity.EmailOrComment;
        var process = await _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = tools.SshKeygen.SelectedPath,
            Arguments = ["-t", "ed25519", "-C", comment, "-f", identity.PrivateKeyPath, "-N", string.Empty],
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);

        if (!process.Succeeded)
        {
            return OperationResult<SshKeyGenerationResult>.Fail(
                "ssh-keygen failed.",
                $"Exit code: {process.ExitCode}",
                process.StandardOutput,
                process.StandardError);
        }

        var publicKey = await ReadPublicKeyAsync(identity, cancellationToken).ConfigureAwait(false);
        return OperationResult<SshKeyGenerationResult>.Ok(new SshKeyGenerationResult
        {
            Identity = identity,
            Process = process,
            PublicKeyText = publicKey.Value ?? string.Empty,
            BackupFiles = backups
        }, "SSH key generated. The key has no passphrase.");
    }

    public async Task<OperationResult> ExportPublicKeyAsync(
        GitHubIdentity identity,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(identity.PublicKeyPath))
        {
            return OperationResult.Fail("Public key file does not exist.", identity.PublicKeyPath);
        }

        if (_fileSystem.FileExists(destinationPath) && !overwrite)
        {
            return OperationResult.Fail("The export destination already exists.", destinationPath);
        }

        await Task.Run(() => _fileSystem.CopyFile(identity.PublicKeyPath, destinationPath, overwrite), cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Public key exported.");
    }

    public async Task<SshTestResult> TestAsync(string hostAlias, bool verbose, CancellationToken cancellationToken = default)
    {
        var validation = HostAliasValidator.Validate(hostAlias);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, validation.Errors), nameof(hostAlias));
        }

        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.Ssh.Exists || string.IsNullOrWhiteSpace(tools.Ssh.SelectedPath))
        {
            var missing = new ProcessResult
            {
                ExecutablePath = "ssh.exe",
                StandardError = "ssh.exe was not found. GitKeyRouter did not attempt to install it."
            };
            return new SshTestResult
            {
                HostAlias = hostAlias,
                Process = missing,
                Classification = "ssh.exe missing"
            };
        }

        var arguments = verbose
            ? new[] { "-vT", $"git@{hostAlias}" }
            : new[] { "-T", $"git@{hostAlias}" };
        var process = await _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = tools.Ssh.SelectedPath,
            Arguments = arguments,
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);
        var combined = process.StandardOutput + Environment.NewLine + process.StandardError;
        var authenticated = combined.Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase);
        return new SshTestResult
        {
            HostAlias = hostAlias,
            Process = process,
            Authenticated = authenticated,
            Classification = Classify(combined, authenticated, process)
        };
    }

    private static string Classify(string output, bool authenticated, ProcessResult process)
    {
        if (authenticated)
        {
            return "GitHub authentication succeeded";
        }

        if (process.TimedOut)
        {
            return "Connection timed out";
        }

        if (output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Public key was rejected";
        }

        if (output.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase))
        {
            return "Host alias or DNS resolution failed";
        }

        if (output.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Host key verification failed";
        }

        return "Unknown SSH result";
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonFileNameCharacterPattern();
}
