using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class GitProfileServiceTests
{
    [Fact]
    public async Task Preview_GeneratesDirectoryAndRemoteConditionalIncludes()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var profile = Profile();
        var configStore = new InMemoryAppConfigStore
        {
            Config = new AppConfig
            {
                GitProfiles = [profile],
                GitProfileRules =
                [
                    new GitProfileRule
                    {
                        ProfileId = profile.Id,
                        Kind = GitProfileRuleKind.Directory,
                        Pattern = Path.Combine(temp.Path, "work", "**")
                    },
                    new GitProfileRule
                    {
                        ProfileId = profile.Id,
                        Kind = GitProfileRuleKind.RemoteUrl,
                        Pattern = "https://gitlab.example/company/**"
                    }
                ]
            }
        };
        var service = CreateService(configStore, paths);

        var preview = await service.BuildPreviewAsync();

        Assert.Contains("[includeIf \"gitdir/i:", preview.MasterConfigText, StringComparison.Ordinal);
        Assert.Contains("[includeIf \"hasconfig:remote.*.url:https://gitlab.example/company/**\"]", preview.MasterConfigText, StringComparison.Ordinal);
        var profileText = Assert.Single(preview.ProfileFiles).Value;
        Assert.Contains("name = \"Camus Work\"", profileText, StringComparison.Ordinal);
        Assert.Contains("email = \"work@example.com\"", profileText, StringComparison.Ordinal);
        Assert.Contains("signingKey = \"ABC123\"", profileText, StringComparison.Ordinal);
        Assert.Contains("gpgSign = true", profileText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_WritesFilesAndRegistersSingleGlobalInclude()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        var profile = Profile();
        var store = new InMemoryAppConfigStore
        {
            Config = new AppConfig
            {
                GitProfiles = [profile],
                GitProfileRules =
                [
                    new GitProfileRule
                    {
                        ProfileId = profile.Id,
                        Kind = GitProfileRuleKind.Directory,
                        Pattern = Path.Combine(temp.Path, "work")
                    }
                ]
            }
        };
        var runner = new StubProcessRunner(request => new ProcessResult
        {
            ExecutablePath = request.ExecutablePath,
            Arguments = request.Arguments,
            ExitCode = request.Arguments.Contains("--get-all") ? 1 : 0
        });
        var service = CreateService(store, paths, runner);
        var preview = await service.BuildPreviewAsync();

        var result = await service.ApplyAsync(preview);

        Assert.True(result.Success);
        Assert.True(File.Exists(service.MasterConfigPath));
        Assert.Single(Directory.GetFiles(service.ProfilesDirectory, "profile-*.gitconfig"));
        Assert.Contains(runner.Requests, request => request.Arguments.SequenceEqual(["config", "--global", "--add", "include.path", service.MasterConfigPath.Replace('\\', '/')]));
    }

    [Fact]
    public void ResolveProfile_UsesLongestDirectoryRuleThenRemoteRule()
    {
        using var temp = new TemporaryDirectory();
        var work = Profile("work", "Work");
        var deep = Profile("deep", "Deep");
        var remote = Profile("remote", "Remote");
        var config = new AppConfig
        {
            GitProfiles = [work, deep, remote],
            GitProfileRules =
            [
                new GitProfileRule { ProfileId = work.Id, Kind = GitProfileRuleKind.Directory, Pattern = temp.Path },
                new GitProfileRule { ProfileId = deep.Id, Kind = GitProfileRuleKind.Directory, Pattern = Path.Combine(temp.Path, "deep") },
                new GitProfileRule { ProfileId = remote.Id, Kind = GitProfileRuleKind.RemoteUrl, Pattern = "git@gitlab.example:company/*" }
            ]
        };
        var service = CreateService(new InMemoryAppConfigStore { Config = config }, new TestAppPaths(temp.Path));

        Assert.Equal(deep.Id, service.ResolveProfile(config, Path.Combine(temp.Path, "deep", "repo"))?.Id);
        Assert.Equal(remote.Id, service.ResolveProfile(config, null, ["git@gitlab.example:company/repo.git"])?.Id);
    }

    private static GitProfileService CreateService(
        InMemoryAppConfigStore store,
        TestAppPaths paths,
        StubProcessRunner? runner = null)
        => new(
            store,
            new NoOpBackupService(),
            new PhysicalFileSystem(),
            paths,
            runner ?? new StubProcessRunner(request => new ProcessResult
            {
                ExecutablePath = request.ExecutablePath,
                Arguments = request.Arguments,
                ExitCode = 0
            }),
            new FixedToolchainService("git.exe"));

    private static GitProfile Profile(string id = "work", string name = "Work")
        => new()
        {
            Id = id,
            DisplayName = name,
            UserName = "Camus Work",
            UserEmail = "work@example.com",
            SigningKey = "ABC123",
            EnableCommitSigning = true
        };
}
