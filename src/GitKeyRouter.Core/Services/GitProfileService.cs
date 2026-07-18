using System.Text;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed class GitProfileService
{
    private const string MasterFileName = "profiles.gitconfig";
    private readonly IAppConfigStore _configStore;
    private readonly IBackupService _backupService;
    private readonly IFileSystem _fileSystem;
    private readonly IAppPaths _paths;
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainService _toolchainService;

    public GitProfileService(
        IAppConfigStore configStore,
        IBackupService backupService,
        IFileSystem fileSystem,
        IAppPaths paths,
        IProcessRunner processRunner,
        IToolchainService toolchainService)
    {
        _configStore = configStore;
        _backupService = backupService;
        _fileSystem = fileSystem;
        _paths = paths;
        _processRunner = processRunner;
        _toolchainService = toolchainService;
    }

    public string ProfilesDirectory => Path.Combine(_paths.AppDataDirectory, "git-profiles");

    public string MasterConfigPath => Path.Combine(ProfilesDirectory, MasterFileName);

    public async Task<OperationResult<GitProfile>> SaveProfileAsync(
        GitProfile profile,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var validation = GitProfileValidator.Validate(profile, config);
        if (!validation.IsValid)
        {
            return OperationResult<GitProfile>.Fail("Git Profile validation failed.", validation.Errors.ToArray());
        }

        profile.DisplayName = profile.DisplayName.Trim();
        profile.UserName = profile.UserName.Trim();
        profile.UserEmail = profile.UserEmail.Trim();
        profile.SigningKey = profile.SigningKey.Trim();
        await _backupService.CreateSnapshotAsync($"Save Git Profile: {profile.DisplayName}", cancellationToken).ConfigureAwait(false);
        var index = config.GitProfiles.FindIndex(item => string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            config.GitProfiles[index] = profile;
        }
        else
        {
            config.GitProfiles.Add(profile);
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult<GitProfile>.Ok(profile, "Git Profile saved.");
    }

    public async Task<OperationResult> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var profile = config.GitProfiles.FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return OperationResult.Fail("Git Profile was not found.");
        }

        await _backupService.CreateSnapshotAsync($"Delete Git Profile: {profile.DisplayName}", cancellationToken).ConfigureAwait(false);
        config.GitProfiles.Remove(profile);
        config.GitProfileRules.RemoveAll(item => string.Equals(item.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Git Profile deleted.");
    }

    public async Task<OperationResult<GitProfileRule>> SaveRuleAsync(
        GitProfileRule rule,
        CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var validation = GitProfileRuleValidator.Validate(rule, config);
        if (!validation.IsValid)
        {
            return OperationResult<GitProfileRule>.Fail("Git Profile rule validation failed.", validation.Errors.ToArray());
        }

        rule.Pattern = GitProfileRuleValidator.NormalizePattern(rule);
        await _backupService.CreateSnapshotAsync("Save Git Profile rule", cancellationToken).ConfigureAwait(false);
        var index = config.GitProfileRules.FindIndex(item => string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            config.GitProfileRules[index] = rule;
        }
        else
        {
            config.GitProfileRules.Add(rule);
        }

        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult<GitProfileRule>.Ok(rule, "Git Profile rule saved.");
    }

    public async Task<OperationResult> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var rule = config.GitProfileRules.FirstOrDefault(item => string.Equals(item.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            return OperationResult.Fail("Git Profile rule was not found.");
        }

        await _backupService.CreateSnapshotAsync("Delete Git Profile rule", cancellationToken).ConfigureAwait(false);
        config.GitProfileRules.Remove(rule);
        await _configStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Git Profile rule deleted.");
    }

    public async Task<GitProfileConfigPreview> BuildPreviewAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var files = BuildProfileFiles(config);
        var master = BuildMasterConfig(config, files);
        var existingMaster = _fileSystem.FileExists(MasterConfigPath)
            ? await _fileSystem.ReadAllTextAsync(MasterConfigPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        var diff = new StringBuilder(TextDiffService.CreateSimpleDiff(
            existingMaster,
            master,
            MasterFileName + ".before",
            MasterFileName + ".after"));
        var hasChanges = !string.Equals(existingMaster, master, StringComparison.Ordinal);
        foreach (var (path, text) in files)
        {
            var original = _fileSystem.FileExists(path)
                ? await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            if (!string.Equals(original, text, StringComparison.Ordinal))
            {
                hasChanges = true;
                diff.AppendLine().Append(TextDiffService.CreateSimpleDiff(
                    original,
                    text,
                    Path.GetFileName(path) + ".before",
                    Path.GetFileName(path) + ".after"));
            }
        }

        return new GitProfileConfigPreview
        {
            MasterConfigPath = MasterConfigPath,
            MasterConfigText = master,
            ProfileFiles = files,
            DiffText = diff.ToString(),
            HasChanges = hasChanges
        };
    }

    public async Task<OperationResult<GitProfileApplyResult>> ApplyAsync(
        GitProfileConfigPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        await _backupService.CreateSnapshotAsync("Apply Git Profile conditional config", cancellationToken).ConfigureAwait(false);
        _fileSystem.CreateDirectory(ProfilesDirectory);
        await _fileSystem.WriteAllTextAtomicAsync(MasterConfigPath, preview.MasterConfigText, cancellationToken).ConfigureAwait(false);
        foreach (var (path, text) in preview.ProfileFiles)
        {
            await _fileSystem.WriteAllTextAtomicAsync(path, text, cancellationToken).ConfigureAwait(false);
        }

        var expectedPaths = preview.ProfileFiles.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _fileSystem.EnumerateFiles(ProfilesDirectory, "profile-*.gitconfig"))
        {
            if (!expectedPaths.Contains(path))
            {
                _fileSystem.DeleteFile(path);
            }
        }

        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.Git.Exists || string.IsNullOrWhiteSpace(tools.Git.SelectedPath))
        {
            return OperationResult<GitProfileApplyResult>.Fail("git.exe was not found; profile files were generated but the global include was not registered.");
        }

        var includePath = ToGitPath(MasterConfigPath);
        var getResult = await _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = tools.Git.SelectedPath,
            Arguments = ["config", "--global", "--get-all", "include.path"]
        }, cancellationToken).ConfigureAwait(false);
        var registered = getResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(NormalizeGitPath(item), NormalizeGitPath(includePath), StringComparison.OrdinalIgnoreCase));
        ProcessResult? registration = null;
        if (!registered)
        {
            registration = await _processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = tools.Git.SelectedPath,
                Arguments = ["config", "--global", "--add", "include.path", includePath]
            }, cancellationToken).ConfigureAwait(false);
            if (!registration.Succeeded)
            {
                return OperationResult<GitProfileApplyResult>.Fail("Failed to register the Git Profile include file.", registration.StandardError);
            }
        }

        return OperationResult<GitProfileApplyResult>.Ok(new GitProfileApplyResult
        {
            MasterConfigPath = MasterConfigPath,
            ProfileFileCount = preview.ProfileFiles.Count,
            IncludeRegistrationResult = registration
        }, "Git Profile conditional config applied.");
    }

    public GitProfile? ResolveProfile(AppConfig config, string? repositoryDirectory, IEnumerable<string>? remoteUrls = null)
    {
        var enabled = config.GitProfileRules.Where(item => item.Enabled).ToList();
        if (!string.IsNullOrWhiteSpace(repositoryDirectory))
        {
            var directory = GitProfileRuleValidator.NormalizeDirectoryPattern(repositoryDirectory);
            var match = enabled.Where(item => item.Kind == GitProfileRuleKind.Directory
                    && directory.StartsWith(GitProfileRuleValidator.NormalizeDirectoryPattern(item.Pattern), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => GitProfileRuleValidator.NormalizeDirectoryPattern(item.Pattern).Length)
                .FirstOrDefault();
            if (match is not null)
            {
                return config.GitProfiles.FirstOrDefault(item => string.Equals(item.Id, match.ProfileId, StringComparison.OrdinalIgnoreCase));
            }
        }

        foreach (var remoteUrl in remoteUrls ?? [])
        {
            var match = enabled.FirstOrDefault(item => item.Kind == GitProfileRuleKind.RemoteUrl
                && MatchesRemotePattern(remoteUrl, item.Pattern));
            if (match is not null)
            {
                return config.GitProfiles.FirstOrDefault(item => string.Equals(item.Id, match.ProfileId, StringComparison.OrdinalIgnoreCase));
            }
        }

        return null;
    }

    private IReadOnlyDictionary<string, string> BuildProfileFiles(AppConfig config)
        => config.GitProfiles.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                profile => ProfilePath(profile.Id),
                profile => BuildProfileConfig(profile, config),
                StringComparer.OrdinalIgnoreCase);

    private string BuildMasterConfig(AppConfig config, IReadOnlyDictionary<string, string> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GitKeyRouter managed Git Profile conditions. Do not edit manually.");
        foreach (var rule in config.GitProfileRules.Where(item => item.Enabled)
                     .OrderBy(item => item.Kind)
                     .ThenBy(item => item.Pattern, StringComparer.OrdinalIgnoreCase))
        {
            if (!files.TryGetValue(ProfilePath(rule.ProfileId), out _))
            {
                continue;
            }

            var condition = rule.Kind == GitProfileRuleKind.Directory
                ? "gitdir/i:" + GitProfileRuleValidator.NormalizeDirectoryPattern(rule.Pattern)
                : "hasconfig:remote.*.url:" + rule.Pattern.Trim();
            builder.Append("[includeIf \"").Append(Escape(condition)).AppendLine("\"]");
            builder.Append("    path = \"").Append(Escape(ToGitPath(ProfilePath(rule.ProfileId)))).AppendLine("\"");
        }

        return builder.ToString();
    }

    private static string BuildProfileConfig(GitProfile profile, AppConfig config)
    {
        var service = config.FindService(profile.DefaultServiceInstanceId);
        var identity = config.Identities.FirstOrDefault(item => string.Equals(item.Id, profile.DefaultIdentityId, StringComparison.OrdinalIgnoreCase));
        var builder = new StringBuilder();
        builder.AppendLine("# GitKeyRouter managed Git Profile. Do not edit manually.");
        builder.Append("# Profile: ").AppendLine(profile.DisplayName);
        if (service is not null)
        {
            builder.Append("# Default service: ").AppendLine(service.DisplayName);
        }

        if (identity is not null)
        {
            builder.Append("# Default SSH identity: ").Append(identity.DisplayName).Append(" (").Append(identity.HostAlias).AppendLine(")");
        }

        builder.AppendLine("[user]");
        builder.Append("    name = \"").Append(Escape(profile.UserName)).AppendLine("\"");
        builder.Append("    email = \"").Append(Escape(profile.UserEmail)).AppendLine("\"");
        if (!string.IsNullOrWhiteSpace(profile.SigningKey))
        {
            builder.Append("    signingKey = \"").Append(Escape(profile.SigningKey)).AppendLine("\"");
        }

        if (profile.EnableCommitSigning)
        {
            builder.AppendLine("[commit]");
            builder.AppendLine("    gpgSign = true");
        }

        return builder.ToString();
    }

    private string ProfilePath(string profileId)
        => Path.Combine(ProfilesDirectory, $"profile-{profileId}.gitconfig");

    private static bool MatchesRemotePattern(string value, string pattern)
    {
        var normalizedPattern = pattern.Trim();
        var wildcard = normalizedPattern.IndexOf('*');
        return wildcard < 0
            ? string.Equals(value, normalizedPattern, StringComparison.OrdinalIgnoreCase)
            : value.StartsWith(normalizedPattern[..wildcard], StringComparison.OrdinalIgnoreCase);
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    private static string ToGitPath(string path)
        => path.Replace('\\', '/');

    private static string NormalizeGitPath(string value)
        => value.Trim().Trim('"').Replace('\\', '/').TrimEnd('/');
}
