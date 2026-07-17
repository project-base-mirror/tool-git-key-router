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

    public async Task<OperationResult<IReadOnlyList<SshPublicKeyVariant>>> ListPublicKeyVariantsAsync(
        GitHubIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_fileSystem.FileExists(identity.PublicKeyPath))
            {
                paths.Add(Path.GetFullPath(identity.PublicKeyPath));
            }

            var directory = Path.GetDirectoryName(identity.PublicKeyPath);
            var stem = GetVariantStem(identity.PublicKeyPath);
            if (!string.IsNullOrWhiteSpace(directory) && _fileSystem.DirectoryExists(directory))
            {
                foreach (var path in _fileSystem.EnumerateFiles(directory, $"{stem}*"))
                {
                    var fileName = Path.GetFileName(path);
                    if (fileName.Contains(".gitkeyrouter.", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith(".gitkeyrouter-", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    paths.Add(Path.GetFullPath(path));
                }
            }

            var variants = new List<SshPublicKeyVariant>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var inspection = SshKeyFormatDetector.Detect(text, path);
                if (inspection.IsPrivateMaterial)
                {
                    continue;
                }

                variants.Add(new SshPublicKeyVariant
                {
                    Path = path,
                    FileName = Path.GetFileName(path),
                    Inspection = inspection,
                    IsConfiguredPath = PathsEqual(path, identity.PublicKeyPath)
                });
            }

            return OperationResult<IReadOnlyList<SshPublicKeyVariant>>.Ok(
                variants
                    .OrderByDescending(item => item.IsConfiguredPath)
                    .ThenBy(item => item.Inspection.Format)
                    .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                $"Detected {variants.Count} public-key variant(s).");
        }
        catch (Exception exception)
        {
            return OperationResult<IReadOnlyList<SshPublicKeyVariant>>.Fail(
                "Unable to enumerate public-key variants.",
                exception.Message);
        }
    }

    public async Task<OperationResult<SshKeyInspectionResult>> InspectKeyFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(path))
        {
            return OperationResult<SshKeyInspectionResult>.Fail("Key file does not exist.", path);
        }

        try
        {
            var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return OperationResult<SshKeyInspectionResult>.Ok(
                SshKeyFormatDetector.Detect(text, path),
                "Key format detected.");
        }
        catch (Exception exception)
        {
            return OperationResult<SshKeyInspectionResult>.Fail("Unable to inspect the key file.", exception.Message);
        }
    }

    public Task<OperationResult<string>> ReadPublicKeyAsync(
        GitHubIdentity identity,
        CancellationToken cancellationToken = default)
        => ReadPublicKeyAsync(identity.PublicKeyPath, requireOpenSsh: false, cancellationToken);

    public async Task<OperationResult<string>> ReadPublicKeyAsync(
        string path,
        bool requireOpenSsh,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectKeyFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success || inspection.Value is null)
        {
            return OperationResult<string>.Fail(inspection.Message, inspection.Errors.ToArray());
        }

        if (inspection.Value.IsPrivateMaterial)
        {
            return OperationResult<string>.Fail(
                "Private key material will not be displayed or copied.",
                "Use format conversion to derive a separate public-key file.");
        }

        if (requireOpenSsh && !inspection.Value.IsOpenSsh)
        {
            return OperationResult<string>.Fail(
                $"The selected key is {inspection.Value.DisplayName}, not an OpenSSH public key.",
                inspection.Value.CanConvert
                    ? "Convert it to OpenSSH before copying it to GitHub."
                    : "Select a valid OpenSSH public-key file.");
        }

        try
        {
            var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return OperationResult<string>.Ok(
                inspection.Value.IsOpenSsh ? inspection.Value.PublicKeyText : text.Trim(),
                $"{inspection.Value.DisplayName} loaded.");
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

    public async Task<OperationResult<SshKeyConversionResult>> ConvertPublicKeyAsync(
        GitHubIdentity identity,
        string sourcePath,
        SshPublicKeyExportFormat targetFormat,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.SshKeygen.Exists || string.IsNullOrWhiteSpace(tools.SshKeygen.SelectedPath))
        {
            return OperationResult<SshKeyConversionResult>.Fail(
                "ssh-keygen.exe was not found.",
                "Enable the Windows OpenSSH Client or install Git for Windows.");
        }

        var inspection = await InspectKeyFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success || inspection.Value is null)
        {
            return OperationResult<SshKeyConversionResult>.Fail(inspection.Message, inspection.Errors.ToArray());
        }

        var source = inspection.Value;
        if (source.IsPrivateMaterial && !PathsEqual(sourcePath, identity.PrivateKeyPath))
        {
            return OperationResult<SshKeyConversionResult>.Fail(
                "Private key data was found outside the configured private-key path. Conversion was refused.",
                "Correct the identity paths before converting.");
        }

        if (!source.CanConvert || source.Format is SshKeyFormat.PuttyPrivate or SshKeyFormat.Unknown)
        {
            return OperationResult<SshKeyConversionResult>.Fail(
                $"{source.DisplayName} cannot be converted by the available OpenSSH toolchain.",
                source.Format == SshKeyFormat.PuttyPrivate
                    ? "Convert the PPK with PuTTYgen first."
                    : "Select an OpenSSH, RFC4716, PEM/PKCS public key, or the configured private key.");
        }

        var openSsh = await ConvertToOpenSshAsync(
            tools.SshKeygen.SelectedPath,
            source,
            cancellationToken).ConfigureAwait(false);
        if (!openSsh.Result.Success || string.IsNullOrWhiteSpace(openSsh.Result.Value))
        {
            return OperationResult<SshKeyConversionResult>.Fail(openSsh.Result.Message, openSsh.Result.Errors.ToArray());
        }

        var convertedText = openSsh.Result.Value;
        ProcessResult? exportProcess = null;
        if (targetFormat != SshPublicKeyExportFormat.OpenSsh)
        {
            var exported = await ExportFromOpenSshAsync(
                tools.SshKeygen.SelectedPath,
                openSsh.Result.Value,
                targetFormat,
                identity.PublicKeyPath,
                cancellationToken).ConfigureAwait(false);
            if (!exported.Result.Success || string.IsNullOrWhiteSpace(exported.Result.Value))
            {
                return OperationResult<SshKeyConversionResult>.Fail(exported.Result.Message, exported.Result.Errors.ToArray());
            }

            convertedText = exported.Result.Value;
            exportProcess = exported.Process;
        }

        var destinationPath = GetVariantPath(identity.PublicKeyPath, targetFormat);
        var converted = SshKeyFormatDetector.Detect(convertedText, destinationPath);
        if (targetFormat == SshPublicKeyExportFormat.OpenSsh && !converted.IsOpenSsh)
        {
            return OperationResult<SshKeyConversionResult>.Fail("The conversion output is not a valid OpenSSH public key.");
        }

        string? backupFile = null;
        if (_fileSystem.FileExists(destinationPath))
        {
            var existing = await _fileSystem.ReadAllTextAsync(destinationPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existing.Trim(), convertedText.Trim(), StringComparison.Ordinal))
            {
                return OperationResult<SshKeyConversionResult>.Ok(new SshKeyConversionResult
                {
                    Source = source,
                    Converted = converted,
                    DestinationPath = destinationPath,
                    ImportProcess = openSsh.Process,
                    ExportProcess = exportProcess,
                    Changed = false
                }, "The requested public-key variant already exists with identical content.");
            }

            if (!overwrite)
            {
                return OperationResult<SshKeyConversionResult>.Fail(
                    "The target public-key variant already exists.",
                    destinationPath);
            }

            backupFile = $"{destinationPath}.gitkeyrouter.{_clock.LocalNow:yyyyMMdd-HHmmss}.bak";
            _fileSystem.CopyFile(destinationPath, backupFile, false);
        }

        await _fileSystem.WriteAllTextAtomicAsync(
            destinationPath,
            convertedText.Trim() + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        return OperationResult<SshKeyConversionResult>.Ok(new SshKeyConversionResult
        {
            Source = source,
            Converted = converted,
            DestinationPath = destinationPath,
            ImportProcess = openSsh.Process,
            ExportProcess = exportProcess,
            BackupFile = backupFile,
            Changed = true
        }, $"Created {converted.DisplayName}: {destinationPath}");
    }

    public Task<OperationResult> ExportPublicKeyAsync(
        GitHubIdentity identity,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
        => ExportPublicKeyAsync(identity.PublicKeyPath, destinationPath, overwrite, cancellationToken);

    public async Task<OperationResult> ExportPublicKeyAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectKeyFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success || inspection.Value is null)
        {
            return OperationResult.Fail(inspection.Message, inspection.Errors.ToArray());
        }

        if (inspection.Value.IsPrivateMaterial)
        {
            return OperationResult.Fail("Private key material cannot be exported through the public-key action.");
        }

        if (_fileSystem.FileExists(destinationPath) && !overwrite)
        {
            return OperationResult.Fail("The export destination already exists.", destinationPath);
        }

        await Task.Run(() => _fileSystem.CopyFile(sourcePath, destinationPath, overwrite), cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok($"{inspection.Value.DisplayName} exported.");
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

    private async Task<(OperationResult<string> Result, ProcessResult? Process)> ConvertToOpenSshAsync(
        string sshKeygenPath,
        SshKeyInspectionResult source,
        CancellationToken cancellationToken)
    {
        if (source.IsOpenSsh)
        {
            return (OperationResult<string>.Ok(source.PublicKeyText), null);
        }

        IReadOnlyList<string> arguments = source.Format switch
        {
            SshKeyFormat.Rfc4716Public => ["-i", "-m", "RFC4716", "-f", source.SourcePath],
            SshKeyFormat.PemPublic => ["-i", "-m", "PKCS8", "-f", source.SourcePath],
            SshKeyFormat.OpenSshPrivate or SshKeyFormat.PemPrivate => ["-y", "-f", source.SourcePath],
            _ => []
        };
        if (arguments.Count == 0)
        {
            return (OperationResult<string>.Fail($"{source.DisplayName} cannot be imported as OpenSSH."), null);
        }

        var process = await RunSshKeygenAsync(sshKeygenPath, arguments, cancellationToken).ConfigureAwait(false);
        if (!process.Succeeded && source.Format == SshKeyFormat.PemPublic)
        {
            process = await RunSshKeygenAsync(
                sshKeygenPath,
                ["-i", "-m", "PEM", "-f", source.SourcePath],
                cancellationToken).ConfigureAwait(false);
        }

        if (!process.Succeeded)
        {
            return (OperationResult<string>.Fail(
                "ssh-keygen could not import the selected key.",
                $"Detected format: {source.DisplayName}",
                $"Exit code: {process.ExitCode}",
                process.StandardError), process);
        }

        if (!SshKeyFormatDetector.TryNormalizeOpenSshPublicKey(process.StandardOutput, out var openSsh, out _))
        {
            return (OperationResult<string>.Fail("ssh-keygen did not return a valid OpenSSH public key."), process);
        }

        return (OperationResult<string>.Ok(openSsh), process);
    }

    private async Task<(OperationResult<string> Result, ProcessResult? Process)> ExportFromOpenSshAsync(
        string sshKeygenPath,
        string openSsh,
        SshPublicKeyExportFormat targetFormat,
        string configuredPublicKeyPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(configuredPublicKeyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return (OperationResult<string>.Fail("The public-key path has no valid parent directory."), null);
        }

        _fileSystem.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".gitkeyrouter-{Guid.NewGuid():N}.openssh.pub");
        try
        {
            await _fileSystem.WriteAllTextAtomicAsync(temporaryPath, openSsh.Trim() + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            var format = targetFormat == SshPublicKeyExportFormat.Rfc4716 ? "RFC4716" : "PKCS8";
            var process = await RunSshKeygenAsync(
                sshKeygenPath,
                ["-e", "-m", format, "-f", temporaryPath],
                cancellationToken).ConfigureAwait(false);
            if (!process.Succeeded)
            {
                return (OperationResult<string>.Fail(
                    "ssh-keygen could not export the requested public-key format.",
                    $"Exit code: {process.ExitCode}",
                    process.StandardError), process);
            }

            return (OperationResult<string>.Ok(process.StandardOutput.Trim()), process);
        }
        finally
        {
            _fileSystem.DeleteFile(temporaryPath);
        }
    }

    private Task<ProcessResult> RunSshKeygenAsync(
        string sshKeygenPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = sshKeygenPath,
            Arguments = arguments,
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken);

    private static string GetVariantStem(string configuredPublicKeyPath)
    {
        var stem = Path.GetFileNameWithoutExtension(configuredPublicKeyPath);
        foreach (var suffix in new[] { ".openssh", ".rfc4716", ".pem" })
        {
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return stem[..^suffix.Length];
            }
        }

        return stem;
    }

    private static string GetVariantPath(string configuredPublicKeyPath, SshPublicKeyExportFormat format)
    {
        var directory = Path.GetDirectoryName(configuredPublicKeyPath) ?? string.Empty;
        var stem = GetVariantStem(configuredPublicKeyPath);
        var suffix = format switch
        {
            SshPublicKeyExportFormat.OpenSsh => ".openssh.pub",
            SshPublicKeyExportFormat.Rfc4716 => ".rfc4716.pub",
            SshPublicKeyExportFormat.Pem => ".pem.pub",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
        return Path.Combine(directory, stem + suffix);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

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
