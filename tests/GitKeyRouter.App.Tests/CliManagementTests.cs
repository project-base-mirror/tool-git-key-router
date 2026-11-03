using System.Text.Json;
using GitKeyRouter.App.Cli;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.Configuration;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.App.Tests;

public sealed class CliManagementTests
{
    [Fact]
    public async Task GiteeIdentityRouteLifecycleUsesServicesAndSeparateApply()
    {
        using var fixture = new CliFixture();
        var templates = await fixture.RunAsync("list-templates", "--json");
        var template = templates.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "Gitee");
        await fixture.SaveAsync("service", template.GetProperty("service").GetRawText());
        var identity = JsonSerializer.Serialize(new
        {
            Id = "gitee-work",
            ServiceInstanceId = "gitee.com",
            DisplayName = "Gitee Work",
            AccountName = "demo",
            HostAlias = "gitee-work",
            PrivateKeyPath = Path.Combine(fixture.Root, "key-that-does-not-exist"),
            PublicKeyPath = Path.Combine(fixture.Root, "key-that-does-not-exist.pub")
        });
        await fixture.SaveAsync("identity", identity);
        const string route = """{"Id":"team-route","ServiceInstanceId":"gitee.com","Scope":"Owner","NamespacePath":"team","IdentityId":"gitee-work"}""";
        await fixture.SaveAsync("route", route);
        await fixture.SaveAsync("route", route); // Stable IDs update in place.
        var config = await fixture.Store.LoadAsync();
        Assert.Single(config.RepositoryRoutes);
        Assert.False(File.Exists(fixture.Paths.SshConfigPath));
        Assert.Empty(fixture.Git.Rules);
        var listed = await fixture.RunAsync("list-routes", "--json");
        Assert.Equal("team-route", listed.GetProperty("data")[0].GetProperty("Id").GetString());
        var parsed = await fixture.RunAsync("parse-url", "git@gitee.com:team/repo.git", "--json");
        Assert.Equal("gitee.com", parsed.GetProperty("data").GetProperty("ServiceInstanceId").GetString());
        var preview = await fixture.RunAsync("apply", "--json");
        Assert.Equal(1, preview.GetProperty("exitCode").GetInt32());
        Assert.False(preview.GetProperty("data").GetProperty("applied").GetBoolean());
        Assert.False(File.Exists(fixture.Paths.SshConfigPath));
        Assert.Empty(fixture.Git.Rules);
        var applied = await fixture.RunAsync("apply", "--yes", "--json");
        Assert.Equal(0, applied.GetProperty("exitCode").GetInt32());
        Assert.Contains("HostName gitee.com", await File.ReadAllTextAsync(fixture.Paths.SshConfigPath));
        Assert.NotEmpty(fixture.Git.Rules);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "key-that-does-not-exist")));
        var blockedDelete = await fixture.RunAsync("delete-service", "gitee.com", "--yes", "--json");
        Assert.Equal(2, blockedDelete.GetProperty("exitCode").GetInt32());
        await fixture.RunAsync("delete-route", "team-route", "--yes", "--json");
        await fixture.RunAsync("delete-identity", "gitee-work", "--yes", "--json");
        await fixture.RunAsync("delete-service", "gitee.com", "--yes", "--json");
        config = await fixture.Store.LoadAsync();
        Assert.Empty(config.Identities);
        Assert.Empty(config.RepositoryRoutes);
        Assert.Single(config.GitServices);
    }

    [Fact]
    public async Task ProfilesAndRulesCanBeSavedResolvedAndDeleted()
    {
        using var fixture = new CliFixture();
        await fixture.SaveAsync("profile", """{"Id":"work","DisplayName":"Work","UserName":"Demo","UserEmail":"demo@example.com"}""");
        var directory = Path.Combine(fixture.Root, "work") + Path.DirectorySeparatorChar;
        await fixture.SaveAsync("profile-rule", JsonSerializer.Serialize(new
        {
            Id = "work-dir",
            ProfileId = "work",
            Kind = "Directory",
            Pattern = directory
        }));
        var rules = await fixture.RunAsync("list-profile-rules", "--json");
        Assert.Equal("work-dir", rules.GetProperty("data")[0].GetProperty("Id").GetString());
        var resolved = await fixture.RunAsync("resolve-profile", Path.Combine(directory, "repo"), "--json");
        Assert.Equal("work", resolved.GetProperty("data").GetProperty("Id").GetString());
        await fixture.RunAsync("delete-profile", "work", "--yes", "--json");
        Assert.Empty((await fixture.Store.LoadAsync()).GitProfileRules);
    }

    [Theory]
    [InlineData("save-route --file unused.json --json")]
    [InlineData("save-service --yes --file --json")]
    [InlineData("delete-identity id --yes --yes --json")]
    [InlineData("apply --yes --typo --json")]
    [InlineData("list-services unexpected --json")]
    [InlineData("parse-url --json")]
    [InlineData("test-route team --connect --json")]
    [InlineData("unknown --json")]
    public async Task InvalidArgumentsReturnOneJsonErrorAndNeverWrite(string command)
    {
        using var fixture = new CliFixture();
        var result = await fixture.RunAsync(command.Split(' '));
        Assert.Equal(3, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("invalid_arguments", result.GetProperty("error").GetProperty("code").GetString());
        Assert.False(File.Exists(fixture.Paths.ConfigPath));
        Assert.Equal(0, fixture.Backup.SnapshotCount);
        Assert.Empty(fixture.Git.Rules);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Id\":\"x\",\"id\":\"y\"}")]
    [InlineData("{\"Id\":\"x\",\"DisplayNmae\":\"typo\"}")]
    [InlineData("{\"Id\":\"x\",\"ProviderKind\":999}")]
    [InlineData("{\"Id\":\"x\",\"HostName\":null}")]
    public async Task InvalidRecordIsRejectedWithoutEchoOrWrite(string json)
    {
        using var fixture = new CliFixture();
        var result = await fixture.SaveAsync("service", json, expectedExitCode: 3);
        Assert.Equal("invalid_json", result.GetProperty("error").GetProperty("code").GetString());
        Assert.False(File.Exists(fixture.Paths.ConfigPath));
        Assert.Equal(0, fixture.Backup.SnapshotCount);
    }

    [Fact]
    public async Task ValidationFailureAndMissingRecordAreStructured()
    {
        using var fixture = new CliFixture();
        await fixture.SaveAsync("identity", """{"Id":"invalid"}""", expectedExitCode: 2);
        var missing = await fixture.RunAsync("delete-route", "missing", "--yes", "--json");
        Assert.Equal(2, missing.GetProperty("exitCode").GetInt32());
        Assert.Equal(0, fixture.Backup.SnapshotCount);
        Assert.False(File.Exists(fixture.Paths.ConfigPath));
    }

    [Fact]
    public async Task JsonNoMatchAndEmptyListsRemainMachineReadable()
    {
        using var fixture = new CliFixture();
        var empty = await fixture.RunAsync("--json", "list-identities");
        Assert.Equal(3, empty.GetProperty("exitCode").GetInt32());
        empty = await fixture.RunAsync("list-identities", "--json");
        Assert.Equal(0, empty.GetProperty("data").GetArrayLength());
        var noMatch = await fixture.RunAsync("parse-url", "--json", "https://unknown.example/team/repo.git");
        Assert.Equal(1, noMatch.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, noMatch.GetProperty("data").ValueKind);
        var missingService = await fixture.RunAsync("test-route", "team", "--service", "missing", "--json");
        Assert.Equal("not_found", missingService.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task JsonOutputRedactsTokensWithoutBreakingJson()
    {
        using var fixture = new CliFixture();
        var config = new AppConfig();
        config.GitProfiles.Add(new GitProfile { Id = "demo", DisplayName = "https://user:secret@example.com/path" });
        await fixture.Store.SaveAsync(config);
        var result = await fixture.RunAsync("list-profiles", "--json");
        Assert.DoesNotContain("user:secret", result.GetRawText());
        Assert.Contains("[REDACTED]", result.GetProperty("data")[0].GetProperty("DisplayName").GetString());
    }

    private sealed class CliFixture : IDisposable
    {
        private readonly TemporaryDirectory _temp = new();
        private readonly CliApplication _cli;
        public string Root => _temp.Path;
        public TestAppPaths Paths { get; }
        public JsonAppConfigStore Store { get; }
        public NoOpBackupService Backup { get; } = new();
        public FakeGitUrlRewriteStore Git { get; } = new();

        public CliFixture()
        {
            Paths = new TestAppPaths(Root);
            var fileSystem = new PhysicalFileSystem();
            Store = new JsonAppConfigStore(Paths, fileSystem);
            var providers = GitProviderAdapterRegistry.CreateDefault();
            var clock = new TestClock();
            var runner = new StubProcessRunner(_ => throw new InvalidOperationException("No real process expected."));
            var tools = new FixedToolchainService("git.exe");
            var ssh = new SshConfigService(fileSystem, Paths, Backup, providers);
            _cli = new CliApplication(new ApplicationServices
            {
                Paths = Paths,
                FileSystem = fileSystem,
                ConfigStore = Store,
                BackupService = Backup,
                GitProviderAdapters = providers,
                ToolchainService = tools,
                GitServiceService = new GitServiceService(Store, Backup, runner, tools, providers),
                IdentityService = new IdentityService(Store, Backup, clock, ssh),
                OwnerRouteService = new OwnerRouteService(Store, Backup, providers),
                GitProfileService = new GitProfileService(Store, Backup, fileSystem, Paths, runner, tools),
                SshConfigService = ssh,
                GitUrlRewriteService = new GitUrlRewriteService(Store, Git, Backup, providers),
                RequiredToolInstallerService = null!,
                PortableBackupService = null!,
                SshKeyService = null!,
                SshHostTrustService = null!,
                GitSshBackendService = null!,
                SshKeyRenameService = null!,
                GitHubCliService = null!,
                DiagnosticService = null!,
                UpdateChecker = null!,
                UpdateDownloadService = null!,
                UpdateInstallerLauncher = null!,
                Logger = null!
            });
        }

        public async Task<JsonElement> SaveAsync(string kind, string json, int expectedExitCode = 0)
        {
            var input = Console.In;
            try
            {
                Console.SetIn(new StringReader(json));
                var result = await RunAsync($"save-{kind}", "--file", "-", "--yes", "--json");
                Assert.Equal(expectedExitCode, result.GetProperty("exitCode").GetInt32());
                return result;
            }
            finally
            {
                Console.SetIn(input);
            }
        }

        public async Task<JsonElement> RunAsync(params string[] args)
        {
            var output = Console.Out;
            var error = Console.Error;
            using var capturedOutput = new StringWriter();
            using var capturedError = new StringWriter();
            try
            {
                Console.SetOut(capturedOutput);
                Console.SetError(capturedError);
                var exitCode = await _cli.RunAsync(args);
                using var document = JsonDocument.Parse(capturedOutput.ToString());
                Assert.Empty(capturedError.ToString());
                Assert.Equal(exitCode, document.RootElement.GetProperty("exitCode").GetInt32());
                Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
                return document.RootElement.Clone();
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }
        }

        public void Dispose() => _temp.Dispose();
    }
}
