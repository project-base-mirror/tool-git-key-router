using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.Git;

public sealed class GitUrlRewriteStore : IGitUrlRewriteStore
{
    private const string InsteadOfSuffix = ".insteadof";
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainService _toolchainService;

    public GitUrlRewriteStore(IProcessRunner processRunner, IToolchainService toolchainService)
    {
        _processRunner = processRunner;
        _toolchainService = toolchainService;
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

    private async Task<string> RequireGitAsync(CancellationToken cancellationToken)
    {
        var toolchain = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!toolchain.Git.Exists || string.IsNullOrWhiteSpace(toolchain.Git.SelectedPath))
        {
            throw new FileNotFoundException("git.exe was not found. Install Git for Windows or add it to PATH; GitKeyRouter will not install it automatically.");
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
            Timeout = timeout ?? TimeSpan.FromSeconds(20)
        }, cancellationToken);
}
