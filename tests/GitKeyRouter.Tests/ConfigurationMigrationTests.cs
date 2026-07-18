using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.Configuration;
using GitKeyRouter.Infrastructure.FileSystem;
using GitKeyRouter.Tests.TestSupport;

namespace GitKeyRouter.Tests;

public sealed class ConfigurationMigrationTests
{
    [Fact]
    public async Task Schema1Migration_PreservesGitHubSshAndRewriteOutputExactly()
    {
        using var temp = new TemporaryDirectory();
        var paths = new TestAppPaths(temp.Path);
        Directory.CreateDirectory(paths.AppDataDirectory);
        var schema1 = """
        {
          "SchemaVersion": 1,
          "Identities": [
            {
              "Id": "camus",
              "DisplayName": "Camus GitHub",
              "GitHubUsername": "camus0109",
              "HostAlias": "github-camus",
              "PrivateKeyPath": "C:\\keys\\camus",
              "PublicKeyPath": "C:\\keys\\camus.pub",
              "EmailOrComment": "camus@example.com",
              "CreatedAt": "2026-07-18T00:00:00+00:00"
            }
          ],
          "OwnerRoutes": [
            {
              "GitHubOwner": "camus0109",
              "IdentityId": "camus",
              "Enabled": true
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(paths.ConfigPath, schema1);

        var store = new JsonAppConfigStore(paths, new PhysicalFileSystem());
        var config = await store.LoadAsync();

        Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
        var service = Assert.Single(config.GitServices);
        Assert.Equal(GitServiceInstance.GitHubComId, service.Id);
        var identity = Assert.Single(config.Identities);
        Assert.Equal("camus0109", identity.AccountName);
        Assert.Equal(service.Id, identity.ServiceInstanceId);
        var route = Assert.Single(config.RepositoryRoutes);
        Assert.Equal("camus0109", route.NamespacePath);
        Assert.Equal(service.Id, route.ServiceInstanceId);

        const string newline = "\r\n";
        var expectedSsh = string.Join(newline,
        [
            "# BEGIN GitKeyRouter managed block: github-camus",
            "Host github-camus",
            "    HostName github.com",
            "    User git",
            "    IdentityFile C:/keys/camus",
            "    IdentitiesOnly yes",
            "# END GitKeyRouter managed block: github-camus",
            string.Empty
        ]);
        Assert.Equal(expectedSsh, SshConfigService.BuildManagedBlock(service, identity, newline));

        var rules = OwnerRouteService.BuildExpectedRules(config);
        Assert.Equal(
        [
            new GitUrlRewriteRule("git@github-camus:camus0109/", "https://github.com/camus0109/"),
            new GitUrlRewriteRule("git@github-camus:camus0109/", "git@github.com:camus0109/")
        ],
        rules);

        await store.SaveAsync(config);
        var saved = await File.ReadAllTextAsync(paths.ConfigPath);
        Assert.Contains("\"SchemaVersion\": 2", saved, StringComparison.Ordinal);
        Assert.Contains("\"GitServices\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"RepositoryRoutes\"", saved, StringComparison.Ordinal);
        Assert.Contains("\"AccountName\": \"camus0109\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OwnerRoutes\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"GitHubUsername\"", saved, StringComparison.Ordinal);
    }
}
