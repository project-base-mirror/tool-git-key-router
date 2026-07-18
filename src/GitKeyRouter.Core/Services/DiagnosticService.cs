using System.Text.RegularExpressions;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public sealed partial class DiagnosticService
{
    private readonly IAppConfigStore _configStore;
    private readonly IAppPaths _paths;
    private readonly IFileSystem _fileSystem;
    private readonly IToolchainService _toolchainService;
    private readonly SshConfigService _sshConfigService;
    private readonly GitUrlRewriteService _gitUrlRewriteService;
    private readonly IClock _clock;
    private readonly GitProviderAdapterRegistry _providers;

    public DiagnosticService(
        IAppConfigStore configStore,
        IAppPaths paths,
        IFileSystem fileSystem,
        IToolchainService toolchainService,
        SshConfigService sshConfigService,
        GitUrlRewriteService gitUrlRewriteService,
        IClock clock,
        GitProviderAdapterRegistry? providers = null)
    {
        _configStore = configStore;
        _paths = paths;
        _fileSystem = fileSystem;
        _toolchainService = toolchainService;
        _sshConfigService = sshConfigService;
        _gitUrlRewriteService = gitUrlRewriteService;
        _clock = clock;
        _providers = providers ?? GitProviderAdapterRegistry.CreateDefault();
    }

    public async Task<DiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = new DiagnosticReport { GeneratedAt = _clock.UtcNow };
        AddEnvironmentItems(report);

        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        AddTool(report, tools.Git);
        AddTool(report, tools.Ssh);
        AddTool(report, tools.SshKeygen);

        AppConfig config;
        try
        {
            config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            Add(report, "APP_CONFIG", "Configuration", "Application configuration", _configStore.ConfigPath,
                DiagnosticSeverity.Normal);
        }
        catch (Exception exception)
        {
            Add(report, "APP_CONFIG_INVALID", "Configuration", "Application configuration cannot be read", exception.Message,
                DiagnosticSeverity.Error, "Correct config.json or restore a backup. GitKeyRouter will not overwrite the damaged file.");
            return report;
        }

        await AddIdentityChecksAsync(report, config, cancellationToken).ConfigureAwait(false);
        await AddSshConfigChecksAsync(report, config, cancellationToken).ConfigureAwait(false);
        await AddGitChecksAsync(report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private void AddEnvironmentItems(DiagnosticReport report)
    {
        Add(report, "WINDOWS_USER", "Environment", "Current Windows user", Environment.UserName, DiagnosticSeverity.Normal);
        Add(report, "USER_HOME", "Environment", "User home directory", _paths.UserProfileDirectory, DiagnosticSeverity.Normal);
        Add(report, "SSH_DIRECTORY", "Environment", ".ssh directory",
            $"{_paths.SshDirectory} (exists: {_fileSystem.DirectoryExists(_paths.SshDirectory)})", DiagnosticSeverity.Normal);
        Add(report, "SSH_CONFIG_PATH", "Environment", "SSH config path",
            $"{_paths.SshConfigPath} (exists: {_fileSystem.FileExists(_paths.SshConfigPath)})", DiagnosticSeverity.Normal);
    }

    private static void AddTool(DiagnosticReport report, ExecutableInfo tool)
    {
        if (!tool.Exists)
        {
            Add(report, $"TOOL_{tool.Name}_MISSING", "Tools", tool.Name,
                "Not found.", DiagnosticSeverity.Error,
                "Use Overview > Detect/install required software, then run diagnostics again.");
            return;
        }

        Add(report, $"TOOL_{tool.Name}_OK", "Tools", tool.Name,
            $"Path: {tool.SelectedPath}{Environment.NewLine}Version: {tool.Version ?? "Unknown"}", DiagnosticSeverity.Normal);
        if (tool.CandidatePaths.Count > 1)
        {
            Add(report, $"TOOL_{tool.Name}_MULTIPLE", "Tools", $"Multiple {tool.Name} candidates",
                string.Join(Environment.NewLine, tool.CandidatePaths), DiagnosticSeverity.Warning,
                "Confirm that the selected executable is the version you intend to use.");
        }
    }

    private async Task AddIdentityChecksAsync(DiagnosticReport report, AppConfig config, CancellationToken cancellationToken)
    {
        foreach (var duplicate in config.Identities.GroupBy(item => item.HostAlias, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            Add(report, "HOST_ALIAS_DUPLICATE", "Identities", "Duplicate HostAlias", duplicate.Key,
                DiagnosticSeverity.Error, "Assign a unique HostAlias to every identity.");
        }

        foreach (var duplicate in config.Identities
                     .Where(item => !string.IsNullOrWhiteSpace(item.PrivateKeyPath))
                     .GroupBy(item => NormalizePath(item.PrivateKeyPath), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Add(report, "PRIVATE_KEY_PATH_SHARED", "Keys", "Private key path is shared by multiple identities",
                $"Path: {duplicate.Key}{Environment.NewLine}Identities: {string.Join(", ", duplicate.Select(item => item.DisplayName))}",
                DiagnosticSeverity.Warning,
                "Use a separate key for each account unless sharing is intentional.");
        }

        foreach (var duplicate in config.Identities
                     .Where(item => !string.IsNullOrWhiteSpace(item.PublicKeyPath))
                     .GroupBy(item => NormalizePath(item.PublicKeyPath), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Add(report, "PUBLIC_KEY_PATH_SHARED", "Keys", "Public key path is shared by multiple identities",
                $"Path: {duplicate.Key}{Environment.NewLine}Identities: {string.Join(", ", duplicate.Select(item => item.DisplayName))}",
                DiagnosticSeverity.Warning,
                "Confirm that these identities are intended to use the same key pair.");
        }

        var publicKeyMaterial = new Dictionary<string, List<(GitIdentity Identity, string Path)>>(StringComparer.Ordinal);

        foreach (var identity in config.Identities)
        {
            Add(report, $"PRIVATE_KEY_{identity.Id}", "Keys", $"Private key: {identity.DisplayName}",
                $"{identity.PrivateKeyPath} (exists: {_fileSystem.FileExists(identity.PrivateKeyPath)})",
                _fileSystem.FileExists(identity.PrivateKeyPath) ? DiagnosticSeverity.Normal : DiagnosticSeverity.Error,
                _fileSystem.FileExists(identity.PrivateKeyPath) ? null : "Generate a key or select the correct private key path.");
            Add(report, $"PUBLIC_KEY_{identity.Id}", "Keys", $"Public key: {identity.DisplayName}",
                $"{identity.PublicKeyPath} (exists: {_fileSystem.FileExists(identity.PublicKeyPath)})",
                _fileSystem.FileExists(identity.PublicKeyPath) ? DiagnosticSeverity.Normal : DiagnosticSeverity.Error,
                _fileSystem.FileExists(identity.PublicKeyPath) ? null : "Generate or import the corresponding public key.");
            if (_fileSystem.FileExists(identity.PublicKeyPath))
            {
                try
                {
                    var publicKeyText = (await _fileSystem.ReadAllTextAsync(identity.PublicKeyPath, cancellationToken).ConfigureAwait(false)).Trim();
                    var looksLikePublicKey = SshKeyFormatDetector.TryNormalizeOpenSshPublicKey(
                        publicKeyText,
                        out var normalizedPublicKey,
                        out _);
                    if (looksLikePublicKey)
                    {
                        var keyParts = normalizedPublicKey.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        var keyMaterial = string.Join(' ', keyParts.Take(2));
                        if (!publicKeyMaterial.TryGetValue(keyMaterial, out var usages))
                        {
                            usages = [];
                            publicKeyMaterial[keyMaterial] = usages;
                        }

                        usages.Add((identity, identity.PublicKeyPath));
                    }

                    Add(report, $"PUBLIC_KEY_READ_{identity.Id}", "Keys", $"Public key readable: {identity.DisplayName}",
                        looksLikePublicKey ? "The file is readable and has an OpenSSH public-key prefix." : "The file is readable but does not look like a normal OpenSSH public key.",
                        looksLikePublicKey ? DiagnosticSeverity.Normal : DiagnosticSeverity.Warning,
                        looksLikePublicKey ? null : "Confirm that PublicKeyPath points to the .pub file, not the private key.");
                }
                catch (Exception exception)
                {
                    Add(report, $"PUBLIC_KEY_READ_{identity.Id}", "Keys", $"Public key unreadable: {identity.DisplayName}",
                        exception.Message, DiagnosticSeverity.Error, "Check file access and select the correct public key path.");
                }
            }
        }

        foreach (var duplicate in publicKeyMaterial.Values.Where(group =>
                     group.Select(item => item.Identity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                     && group.Select(item => NormalizePath(item.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            Add(report, "PUBLIC_KEY_MATERIAL_REUSED", "Keys", "Different files contain the same public key",
                string.Join(Environment.NewLine, duplicate.Select(item => $"{item.Identity.DisplayName}: {item.Path}")),
                DiagnosticSeverity.Warning,
                "These accounts use copies of the same key pair. Generate separate keys unless this is intentional.");
        }

        foreach (var group in config.RepositoryRoutes.Where(item => item.Enabled)
                     .GroupBy(
                         item => $"{item.ServiceInstanceId}\n{item.NamespacePath}",
                         StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(item => item.IdentityId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            var route = group.First();
            Add(report, "NAMESPACE_MULTIPLE_IDENTITIES", "Routes", "Namespace points to multiple identities",
                $"{route.ServiceInstanceId}/{route.NamespacePath}", DiagnosticSeverity.Error,
                "Keep only one enabled identity route for each service namespace.");
        }

        foreach (var identity in config.Identities)
        {
            if (config.FindService(identity.ServiceInstanceId) is null)
            {
                Add(report, "IDENTITY_SERVICE_MISSING", "Identities", $"Identity: {identity.DisplayName}",
                    $"Git service '{identity.ServiceInstanceId}' does not exist.", DiagnosticSeverity.Error,
                    "Select an existing Git service for the identity.");
            }
        }

        foreach (var route in config.RepositoryRoutes.Where(item => item.Enabled))
        {
            var service = config.FindService(route.ServiceInstanceId);
            if (service is null)
            {
                Add(report, "ROUTE_SERVICE_MISSING", "Routes", $"Repository route: {route.NamespacePath}",
                    $"Git service '{route.ServiceInstanceId}' does not exist.", DiagnosticSeverity.Error,
                    "Select an existing Git service or disable the route.");
                continue;
            }

            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            if (identity is null)
            {
                Add(report, "ROUTE_IDENTITY_MISSING", "Routes", $"Repository route: {route.NamespacePath}",
                    "The referenced identity does not exist.", DiagnosticSeverity.Error,
                    "Select an existing identity or disable the route.");
            }
            else if (!string.Equals(identity.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                Add(report, "ROUTE_IDENTITY_SERVICE_MISMATCH", "Routes", $"Repository route: {route.NamespacePath}",
                    $"Identity '{identity.DisplayName}' belongs to '{identity.ServiceInstanceId}', not '{service.Id}'.",
                    DiagnosticSeverity.Error, "Select an identity from the same Git service.");
            }
        }
    }

    private async Task AddSshConfigChecksAsync(DiagnosticReport report, AppConfig config, CancellationToken cancellationToken)
    {
        var raw = await _sshConfigService.ReadRawAsync(cancellationToken).ConfigureAwait(false);
        var blocks = _sshConfigService.ParseManagedBlocks(raw);
        foreach (var orphan in blocks.Where(block => !config.Identities.Any(identity => string.Equals(identity.HostAlias, block.HostAlias, StringComparison.OrdinalIgnoreCase))))
        {
            Add(report, "SSH_BLOCK_ORPHAN", "SSH Config", "Managed block has no identity", orphan.HostAlias,
                DiagnosticSeverity.Warning, "Review the block and remove it through SSH Config if it is no longer needed.");
        }
        foreach (var duplicate in blocks.GroupBy(item => item.HostAlias, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            Add(report, "SSH_BLOCK_DUPLICATE", "SSH Config", "Duplicate managed block", duplicate.Key,
                DiagnosticSeverity.Error, "Remove the duplicate managed block before applying changes.");
        }

        var unmanaged = RemoveManagedBlocks(raw, blocks);
        foreach (var identity in config.Identities)
        {
            var managedCount = blocks.Count(item => string.Equals(item.HostAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase));
            Add(report, $"SSH_BLOCK_{identity.Id}", "SSH Config", $"Managed Host: {identity.HostAlias}",
                $"Matching managed blocks: {managedCount}", managedCount == 1 ? DiagnosticSeverity.Normal : DiagnosticSeverity.Error,
                managedCount == 1 ? null : "Use SSH Config > Sync identity after reviewing the diff.");

            var service = config.FindService(identity.ServiceInstanceId);
            var managedBlock = blocks.FirstOrDefault(item =>
                string.Equals(item.HostAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase));
            if (service is not null && managedCount == 1 && managedBlock is not null)
            {
                var expected = _providers.Get(service.ProviderKind)
                    .BuildSshManagedBlock(service, identity, DetectNewline(managedBlock.RawText));
                if (!NormalizeNewlines(managedBlock.RawText).Equals(NormalizeNewlines(expected), StringComparison.Ordinal))
                {
                    Add(report, "SSH_BLOCK_SERVICE_MISMATCH", "SSH Config", $"Managed Host differs: {identity.HostAlias}",
                        $"Expected service endpoint: {service.SshUser}@{service.HostName}:{service.SshPort ?? 22}",
                        DiagnosticSeverity.Error, "Synchronize the identity SSH block after reviewing the diff.");
                }
            }

            if (ContainsHost(unmanaged, identity.HostAlias))
            {
                Add(report, "SSH_UNMANAGED_HOST_CONFLICT", "SSH Config", "Unmanaged HostAlias conflict", identity.HostAlias,
                    DiagnosticSeverity.Warning,
                    "Rename the identity HostAlias or manually resolve the existing unmanaged SSH Host entry.");
            }
        }
    }

    private async Task AddGitChecksAsync(DiagnosticReport report, CancellationToken cancellationToken)
    {
        try
        {
            var originsResult = await _gitUrlRewriteService.GetGlobalConfigOriginsAsync(cancellationToken).ConfigureAwait(false);
            if (originsResult.Success)
            {
                var origins = originsResult.Value ?? [];
                Add(report, "GIT_GLOBAL_CONFIG_PATH", "Environment", "Git global configuration path",
                    origins.Count == 0 ? "No populated global Git config origin was reported by git.exe." : string.Join(Environment.NewLine, origins),
                    origins.Count == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Normal,
                    origins.Count == 0 ? "This can be normal before the first global Git setting is written." : null);
            }
            else
            {
                Add(report, "GIT_GLOBAL_CONFIG_PATH_FAILED", "Environment", "Git global configuration path",
                    string.Join(Environment.NewLine, originsResult.Errors), DiagnosticSeverity.Warning,
                    "Verify git.exe and run diagnostics again.");
            }

            var comparisons = await _gitUrlRewriteService.CompareAsync(cancellationToken).ConfigureAwait(false);
            foreach (var comparison in comparisons)
            {
                var severity = comparison.Status switch
                {
                    GitRewriteStatus.Correct => DiagnosticSeverity.Normal,
                    GitRewriteStatus.Extra => DiagnosticSeverity.Warning,
                    GitRewriteStatus.Missing => DiagnosticSeverity.Error,
                    GitRewriteStatus.Duplicate => DiagnosticSeverity.Error,
                    GitRewriteStatus.Conflict => DiagnosticSeverity.Error,
                    _ => DiagnosticSeverity.Warning
                };
                Add(report, $"GIT_REWRITE_{comparison.Status}", "Git URL rewrite",
                    comparison.NamespacePath is null
                        ? comparison.InsteadOfUrl
                        : $"{comparison.ServiceInstanceId}/{comparison.NamespacePath}",
                    $"Status: {comparison.Status}{Environment.NewLine}Base: {comparison.ExpectedBaseUrl}{Environment.NewLine}insteadOf: {comparison.InsteadOfUrl}{Environment.NewLine}Matches: {comparison.ActualMatchCount}",
                    severity,
                    severity == DiagnosticSeverity.Normal ? null : "Review the Git rewrite diff before applying a repair.");
            }
        }
        catch (Exception exception)
        {
            Add(report, "GIT_REWRITE_READ_FAILED", "Git URL rewrite", "Unable to read Git URL rewrites",
                exception.Message, DiagnosticSeverity.Error, "Verify git.exe and the global Git configuration, then run diagnostics again.");
        }
    }

    private static string RemoveManagedBlocks(string raw, IReadOnlyList<SshManagedBlock> blocks)
    {
        var result = raw;
        foreach (var block in blocks.OrderByDescending(item => item.StartIndex))
        {
            result = result.Remove(block.StartIndex, block.Length);
        }

        return result;
    }

    private static bool ContainsHost(string text, string hostAlias)
        => Regex.IsMatch(text, $"(?im)^\\s*Host\\s+{Regex.Escape(hostAlias)}(?:\\s|$)", RegexOptions.CultureInvariant);

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string DetectNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static void Add(
        DiagnosticReport report,
        string code,
        string category,
        string title,
        string message,
        DiagnosticSeverity severity,
        string? action = null)
        => report.Items.Add(new DiagnosticItem
        {
            Code = code,
            Category = category,
            Title = title,
            Message = message,
            Severity = severity,
            SuggestedAction = action
        });
}
