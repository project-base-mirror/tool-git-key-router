using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.Git;
using GitKeyRouter.Infrastructure.ProcessExecution;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GitUrlRewriteStoreIntegrationTests
{
    [Fact]
    public async Task UsesIsolatedGlobalConfigFile()
    {
        var gitPath = FindGit();
        if (gitPath is null)
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var isolatedConfig = Path.Combine(temp.Path, "global.gitconfig");
        var store = new GitUrlRewriteStore(
            new ProcessRunner(),
            new FixedToolchainService(gitPath),
            new Dictionary<string, string?> { ["GIT_CONFIG_GLOBAL"] = isolatedConfig });
        var rule = new GitUrlRewriteRule("git@github-test:owner/", "https://github.com/owner/");

        Assert.True((await store.AddAsync(rule)).Succeeded);
        Assert.Contains(rule, await store.GetAllAsync());
        Assert.True((await store.RemoveAllAsync(rule)).Succeeded);
        Assert.Empty(await store.GetAllAsync());
    }

    private static string? FindGit()
    {
        var executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
