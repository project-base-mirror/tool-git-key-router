using System.Text.Json;
using System.Text.Json.Serialization;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    private const string ManifestFileName = "manifest.json";
    private const string AppConfigFileName = "app_config.json";
    private const string SshConfigFileName = "ssh_config.txt";
    private const string GitRewritesFileName = "git_url_rewrites.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAppPaths _paths;
    private readonly IFileSystem _fileSystem;
    private readonly IGitUrlRewriteStore _gitStore;
    private readonly IClock _clock;

    public BackupService(IAppPaths paths, IFileSystem fileSystem, IGitUrlRewriteStore gitStore, IClock clock)
    {
        _paths = paths;
        _fileSystem = fileSystem;
        _gitStore = gitStore;
        _clock = clock;
    }

    public async Task<BackupManifest> CreateSnapshotAsync(string reason, CancellationToken cancellationToken = default)
    {
        _fileSystem.CreateDirectory(_paths.BackupRootDirectory);
        var directory = CreateUniqueDirectoryName();
        _fileSystem.CreateDirectory(directory);

        var appExists = _fileSystem.FileExists(_paths.ConfigPath);
        var sshExists = _fileSystem.FileExists(_paths.SshConfigPath);
        int? appConfigSchemaVersion = null;
        if (appExists)
        {
            _fileSystem.CopyFile(_paths.ConfigPath, Path.Combine(directory, AppConfigFileName), true);
            var appConfigText = await _fileSystem.ReadAllTextAsync(_paths.ConfigPath, cancellationToken).ConfigureAwait(false);
            appConfigSchemaVersion = TryReadAppConfigSchemaVersion(appConfigText);
        }

        if (sshExists)
        {
            _fileSystem.CopyFile(_paths.SshConfigPath, Path.Combine(directory, SshConfigFileName), true);
        }

        IReadOnlyList<GitUrlRewriteRule> rewrites = [];
        string? gitCaptureError = null;
        try
        {
            rewrites = await _gitStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            gitCaptureError = exception.Message;
        }

        await _fileSystem.WriteAllTextAtomicAsync(
            Path.Combine(directory, GitRewritesFileName),
            JsonSerializer.Serialize(rewrites, JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        var manifest = new BackupManifest
        {
            CreatedAt = _clock.UtcNow,
            Reason = reason,
            BackupDirectory = directory,
            ApplicationVersion = typeof(BackupService).Assembly.GetName().Version?.ToString(),
            AppConfigExisted = appExists,
            AppConfigSchemaVersion = appConfigSchemaVersion,
            SshConfigExisted = sshExists,
            GitRewriteCount = rewrites.Count,
            GitRewriteCaptureError = gitCaptureError
        };
        await _fileSystem.WriteAllTextAtomicAsync(
            Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    public async Task<IReadOnlyList<BackupManifest>> ListAsync(CancellationToken cancellationToken = default)
    {
        var manifests = new List<BackupManifest>();
        foreach (var directory in _fileSystem.EnumerateDirectories(_paths.BackupRootDirectory))
        {
            var path = Path.Combine(directory, ManifestFileName);
            if (!_fileSystem.FileExists(path))
            {
                continue;
            }

            try
            {
                var text = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<BackupManifest>(text, JsonOptions);
                if (manifest is not null)
                {
                    manifest.BackupDirectory = directory;
                    manifests.Add(manifest);
                }
            }
            catch
            {
                // A damaged backup is skipped instead of hiding all usable backups.
            }
        }

        return manifests.OrderByDescending(item => item.CreatedAt).ToList();
    }

    public async Task<BackupSnapshot> ReadAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        if (!_fileSystem.FileExists(manifestPath))
        {
            throw new FileNotFoundException("Backup manifest was not found.", manifestPath);
        }

        var manifestText = await _fileSystem.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestText, JsonOptions)
            ?? throw new InvalidDataException("Backup manifest is invalid.");
        manifest.BackupDirectory = backupDirectory;

        var appPath = Path.Combine(backupDirectory, AppConfigFileName);
        var sshPath = Path.Combine(backupDirectory, SshConfigFileName);
        var gitPath = Path.Combine(backupDirectory, GitRewritesFileName);
        var appText = _fileSystem.FileExists(appPath)
            ? await _fileSystem.ReadAllTextAsync(appPath, cancellationToken).ConfigureAwait(false)
            : null;
        var sshText = _fileSystem.FileExists(sshPath)
            ? await _fileSystem.ReadAllTextAsync(sshPath, cancellationToken).ConfigureAwait(false)
            : null;
        var gitText = _fileSystem.FileExists(gitPath)
            ? await _fileSystem.ReadAllTextAsync(gitPath, cancellationToken).ConfigureAwait(false)
            : "[]";
        var rewrites = JsonSerializer.Deserialize<List<GitUrlRewriteRule>>(gitText, JsonOptions) ?? [];
        return new BackupSnapshot
        {
            Manifest = manifest,
            AppConfigText = appText,
            SshConfigText = sshText,
            GitUrlRewrites = rewrites
        };
    }

    public async Task<OperationResult> RestoreAppConfigAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        if (snapshot.Manifest.AppConfigExisted)
        {
            if (snapshot.AppConfigText is null)
            {
                return OperationResult.Fail("The selected backup is missing its application configuration file.");
            }

            var validation = ValidateAppConfigForRestore(snapshot.AppConfigText);
            if (!validation.Success)
            {
                return validation;
            }
        }

        await CreateSnapshotAsync("Before restoring application configuration", cancellationToken).ConfigureAwait(false);
        if (!snapshot.Manifest.AppConfigExisted)
        {
            _fileSystem.DeleteFile(_paths.ConfigPath);
        }
        else if (snapshot.AppConfigText is not null)
        {
            await _fileSystem.WriteAllTextAtomicAsync(_paths.ConfigPath, snapshot.AppConfigText, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok("Application configuration restored.");
    }

    public async Task<OperationResult> RestoreSshConfigAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        await CreateSnapshotAsync("Before restoring SSH config", cancellationToken).ConfigureAwait(false);
        if (!snapshot.Manifest.SshConfigExisted)
        {
            _fileSystem.DeleteFile(_paths.SshConfigPath);
        }
        else if (snapshot.SshConfigText is not null)
        {
            await _fileSystem.WriteAllTextAtomicAsync(_paths.SshConfigPath, snapshot.SshConfigText, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok("SSH config restored.");
    }

    public async Task<OperationResult> RestoreGitRewritesAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadAsync(backupDirectory, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(snapshot.Manifest.GitRewriteCaptureError))
        {
            return OperationResult.Fail(
                "The selected backup does not contain a reliable Git URL rewrite snapshot.",
                snapshot.Manifest.GitRewriteCaptureError);
        }

        await CreateSnapshotAsync("Before restoring Git URL rewrites", cancellationToken).ConfigureAwait(false);
        var current = await _gitStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rule in current.Distinct())
        {
            var result = await _gitStore.RemoveAllAsync(rule, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded && result.ExitCode != 5)
            {
                return OperationResult.Fail("Failed to remove an existing Git URL rewrite.", result.StandardError);
            }
        }

        foreach (var rule in snapshot.GitUrlRewrites)
        {
            var result = await _gitStore.AddAsync(rule, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return OperationResult.Fail("Failed to restore a Git URL rewrite.", result.StandardError);
            }
        }

        return OperationResult.Ok("Git URL rewrites restored from the selected snapshot.");
    }

    private static int? TryReadAppConfigSchemaVersion(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.TryGetProperty("SchemaVersion", out var schemaVersion)
                ? schemaVersion.GetInt32()
                : 1;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OperationResult ValidateAppConfigForRestore(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OperationResult.Fail("The backup application configuration is not a JSON object.");
            }

            var schemaVersion = document.RootElement.TryGetProperty("SchemaVersion", out var schemaElement)
                ? schemaElement.GetInt32()
                : 1;
            if (schemaVersion < 1)
            {
                return OperationResult.Fail($"The backup application configuration has invalid schema version {schemaVersion}.");
            }

            if (schemaVersion > AppConfig.CurrentSchemaVersion)
            {
                return OperationResult.Fail(
                    $"The backup uses application configuration schema {schemaVersion}, but this version supports up to schema {AppConfig.CurrentSchemaVersion}.");
            }

            if (schemaVersion == AppConfig.CurrentSchemaVersion)
            {
                var config = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions);
                if (config is null)
                {
                    return OperationResult.Fail("The backup application configuration is empty.");
                }

                config.Normalize();
            }

            return OperationResult.Ok("Application configuration is compatible.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return OperationResult.Fail("The backup application configuration is invalid.", exception.Message);
        }
    }

    private string CreateUniqueDirectoryName()
    {
        var baseName = _clock.LocalNow.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(_paths.BackupRootDirectory, baseName);
        var suffix = 1;
        while (_fileSystem.DirectoryExists(candidate))
        {
            candidate = Path.Combine(_paths.BackupRootDirectory, $"{baseName}-{suffix++}");
        }

        return candidate;
    }
}
