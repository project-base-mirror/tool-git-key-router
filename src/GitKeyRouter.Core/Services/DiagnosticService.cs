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

    public DiagnosticService(
        IAppConfigStore configStore,
        IAppPaths paths,
        IFileSystem fileSystem,
        IToolchainService toolchainService,
        SshConfigService sshConfigService,
        GitUrlRewriteService gitUrlRewriteService,
        IClock clock)
    {
        _configStore = configStore;
        _paths = paths;
        _fileSystem = fileSystem;
        _toolchainService = toolchainService;
        _sshConfigService = sshConfigService;
        _gitUrlRewriteService = gitUrlRewriteService;
        _clock = clock;
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

        AddIdentityChecks(report, config);
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
        Add(report, "GIT_CONFIG_HINT", "Environment", "Git global configuration",
            "Resolved by git.exe through --global. GitKeyRouter does not replace the complete .gitconfig file.", DiagnosticSeverity.Normal);
    }

    private static void AddTool(DiagnosticReport report, ExecutableInfo tool)
    {
        if (!tool.Exists)
        {
            Add(report, $"TOOL_{tool.Name}_MISSING", "Tools", tool.Name,
                "Not found. GitKeyRouter did not install or modify software.", DiagnosticSeverity.Error,
                "Install or enable the required tool and run diagnostics again.");
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

    private void AddIdentityChecks(DiagnosticReport report, AppConfig config)
    {
        foreach (var duplicate in config.Identities.GroupBy(item => item.HostAlias, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            Add(report, "HOST_ALIAS_DUPLICATE", "Identities", "Duplicate HostAlias", duplicate.Key,
                DiagnosticSeverity.Error, "Assign a unique HostAlias to every identity.");
        }

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
        }

        foreach (var group in config.OwnerRoutes.Where(item => item.Enabled)
                     .GroupBy(item => item.GitHubOwner, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Select(item => item.IdentityId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            Add(report, "OWNER_MULTIPLE_IDENTITIES", "Routes", "Owner points to multiple identities", group.Key,
                DiagnosticSeverity.Error, "Keep only one enabled identity route for each GitHub Owner.");
        }

        foreach (var route in config.OwnerRoutes.Where(item => item.Enabled))
        {
            if (!config.Identities.Any(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase)))
            {
                Add(report, "ROUTE_IDENTITY_MISSING", "Routes", $"Owner route: {route.GitHubOwner}",
                    "The referenced identity does not exist.", DiagnosticSeverity.Error,
                    "Select an existing identity or disable the route.");
            }
        }
    }

    private async Task AddSshConfigChecksAsync(DiagnosticReport report, AppConfig config, CancellationToken cancellationToken)
    {
        var raw = await _sshConfigService.ReadRawAsync(cancellationToken).ConfigureAwait(false);
        var blocks = _sshConfigService.ParseManagedBlocks(raw);
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
                    comparison.GitHubOwner ?? comparison.InsteadOfUrl,
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
