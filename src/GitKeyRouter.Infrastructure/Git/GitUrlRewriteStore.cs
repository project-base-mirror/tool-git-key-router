using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.Git;

public sealed class GitUrlRewriteStore : IGitUrlRewriteStore
{
    private const string InsteadOfSuffix = ".insteadof";
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainService _toolchainService;
    private readonly IReadOnlyDictionary<string, string?> _environmentVariables;

    public GitUrlRewriteStore(
        IProcessRunner processRunner,
        IToolchainService toolchainService,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        _processRunner = processRunner;
        _toolchainService = toolchainService;
        _environmentVariables = environmentVariables ?? new Dictionary<string, string?>();
    }

    public string? GitExecutablePath { get; private set; }

    public async Task<IReadOnlyList<GitUrlRewriteRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var git = await RequireGitAsync(cancellationToken).ConfigureAwait(false);
        var result = await RunGitAsync(git, ["config", "--global", "--get-regexp", "^url\\..*\\.insteadof$"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return [];
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Unable to read global Git URL rewrites. Exit code: {result.ExitCode}. {result.StandardError}");
        }

        return Parse(result.StandardOutput);
    }

    public async Task<OperationResult<IReadOnlyList<string>>> GetGlobalConfigOriginsAsync(CancellationToken cancellationToken = default)
    {
        var git = await RequireGitAsync(cancellationToken).ConfigureAwait(false);
        var result = await RunGitAsync(git, ["config", "--global", "--show-origin", "--list"], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return OperationResult<IReadOnlyList<string>>.Fail(
                "Unable to resolve the Git global configuration origin.",
                $"Exit code: {result.ExitCode}",
                result.StandardOutput,
                result.StandardError);
        }

        var origins = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExtractOrigin)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? value[5..] : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (origins.Count == 0)
        {
            origins.Add(ResolveDefaultGlobalConfigPath());
        }
        return OperationResult<IReadOnlyList<string>>.Ok(origins, "Git global configuration origins resolved.");
    }

    public async Task<ProcessResult> AddAsync(GitUrlRewriteRule rule, CancellationToken cancellationToken = default)
    {
        var git = await RequireGitAsync(cancellationToken).ConfigureAwait(false);
        return await RunGitAsync(git, ["config", "--global", "--add", rule.ConfigKey, rule.InsteadOfUrl], cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> RemoveAllAsync(GitUrlRewriteRule rule, CancellationToken cancellationToken = default)
    {
        var git = await RequireGitAsync(cancellationToken).ConfigureAwait(false);
        return await RunGitAsync(git, ["config", "--global", "--fixed-value", "--unset-all", rule.ConfigKey, rule.InsteadOfUrl], cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> TestRemoteAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        var git = await RequireGitAsync(cancellationToken).ConfigureAwait(false);
        return await RunGitAsync(git, ["ls-remote", originalUrl, "HEAD"], cancellationToken, TimeSpan.FromSeconds(45)).ConfigureAwait(false);
    }

    public static IReadOnlyList<GitUrlRewriteRule> Parse(string output)
    {
        var result = new List<GitUrlRewriteRule>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                || !key.EndsWith(InsteadOfSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var baseUrl = key[4..^InsteadOfSuffix.Length];
            result.Add(new GitUrlRewriteRule(baseUrl, value));
        }

        return result;
    }

    private static string? ExtractOrigin(string line)
    {
        var tabIndex = line.IndexOf('\t');
        if (tabIndex > 0)
        {
            return line[..tabIndex].Trim().Trim('"');
        }

        var markerIndex = line.IndexOf(" url.", StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            return line[..markerIndex].Trim().Trim('"');
        }

        var separator = line.IndexOf(' ');
        return separator > 0 ? line[..separator].Trim().Trim('"') : null;
    }

    private string ResolveDefaultGlobalConfigPath()
    {
        if (_environmentVariables.TryGetValue("GIT_CONFIG_GLOBAL", out var configured)
            && !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var environmentOverride = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return environmentOverride;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dotGitConfig = Path.Combine(home, ".gitconfig");
        var xdgConfig = Path.Combine(home, ".config", "git", "config");
        return File.Exists(xdgConfig) && !File.Exists(dotGitConfig) ? xdgConfig : dotGitConfig;
    }

    private async Task<string> RequireGitAsync(CancellationToken cancellationToken)
    {
        var toolchain = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!toolchain.Git.Exists || string.IsNullOrWhiteSpace(toolchain.Git.SelectedPath))
        {
            throw new FileNotFoundException("git.exe was not found. Use Overview > Detect/install required software, or add Git to PATH.");
        }

        GitExecutablePath = toolchain.Git.SelectedPath;
        return GitExecutablePath;
    }

    private Task<ProcessResult> RunGitAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
        => _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = executablePath,
            Arguments = arguments,
            Timeout = timeout ?? TimeSpan.FromSeconds(20),
            EnvironmentVariables = _environmentVariables
        }, cancellationToken);
}
