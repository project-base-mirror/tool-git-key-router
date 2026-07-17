using System.Text.Json;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.Configuration;

public sealed class JsonAppConfigStore : IAppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAppConfigStore(IAppPaths paths, IFileSystem fileSystem)
    {
        ConfigPath = paths.ConfigPath;
        _fileSystem = fileSystem;
    }

    public string ConfigPath { get; }

    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_fileSystem.FileExists(ConfigPath))
            {
                return new AppConfig();
            }

            var text = await _fileSystem.ReadAllTextAsync(ConfigPath, cancellationToken).ConfigureAwait(false);
            try
            {
                var config = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions)
                    ?? throw new InvalidDataException("The application configuration is empty.");
                config.Identities ??= [];
                config.OwnerRoutes ??= [];
                return config;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"GitKeyRouter could not parse '{ConfigPath}'. The file was not modified. Restore a backup or correct the JSON.",
                    exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = JsonSerializer.Serialize(config, JsonOptions) + Environment.NewLine;
            await _fileSystem.WriteAllTextAtomicAsync(ConfigPath, text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
