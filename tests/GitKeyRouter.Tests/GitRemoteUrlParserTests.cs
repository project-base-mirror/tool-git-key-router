using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.Tests;

public sealed class GitRemoteUrlParserTests
{
    private readonly GitServiceInstance _service = new()
    {
        Id = "gitlab-office",
        DisplayName = "Office GitLab",
        ProviderKind = GitProviderKind.GitLab,
        HostName = "gitlab.office.example",
        SshPort = 2222,
        SshUser = "git",
        WebBaseUrl = "https://gitlab.office.example/forge",
        AllowInsecureHttp = true,
        EnableExtendedSshUrlRewrites = true
    };

    [Theory]
    [InlineData("https://gitlab.office.example/forge/company/platform/repo.git", GitRemoteUrlPatternKind.Web)]
    [InlineData("http://gitlab.office.example/forge/company/platform/repo.git", GitRemoteUrlPatternKind.Web)]
    [InlineData("git@gitlab.office.example:company/platform/repo.git", GitRemoteUrlPatternKind.Scp)]
    [InlineData("ssh://git@gitlab.office.example:2222/company/platform/repo.git", GitRemoteUrlPatternKind.Ssh)]
    [InlineData("git+ssh://git@gitlab.office.example:2222/company/platform/repo.git", GitRemoteUrlPatternKind.GitSsh)]
    public void Parse_RecognizesSupportedRemoteFormats(string url, GitRemoteUrlPatternKind expectedKind)
    {
        var parser = new GitRemoteUrlParser();

        var result = parser.Parse(url, [_service]);

        Assert.NotNull(result);
        Assert.Equal(_service.Id, result.ServiceInstanceId);
        Assert.Equal(expectedKind, result.PatternKind);
        Assert.Equal("company/platform", result.NamespacePath);
        Assert.Equal("repo", result.RepositoryName);
    }

    [Fact]
    public void Patterns_DeclareWebSubPathHttpAndCustomPort()
    {
        var patterns = GitProviderAdapterRegistry.CreateDefault()
            .Get(GitProviderKind.GitLab)
            .GetSupportedRemotePatterns(_service);

        Assert.Contains(patterns, item => item.Kind == GitRemoteUrlPatternKind.Web && item.HasWebSubPath);
        Assert.Contains(patterns, item => item.Kind == GitRemoteUrlPatternKind.Web && item.IsInsecureHttp);
        Assert.Contains(patterns, item => item.Kind == GitRemoteUrlPatternKind.Ssh && item.IncludesPort);
        Assert.Contains(patterns, item => item.Kind == GitRemoteUrlPatternKind.GitSsh && item.IncludesPort);
    }

    [Fact]
    public void LegacyGitHubService_KeepsOriginalTwoRewriteRules()
    {
        var service = GitServiceInstance.CreateGitHubCom();
        var identity = new GitIdentity { HostAlias = "github-camus" };
        var route = new RepositoryRoute { NamespacePath = "camus", IdentityId = identity.Id };
        var rules = GitProviderAdapterRegistry.CreateDefault()
            .Get(GitProviderKind.GitHub)
            .BuildRewriteRules(service, identity, route);

        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, item => item.InsteadOfUrl == "https://github.com/camus/");
        Assert.Contains(rules, item => item.InsteadOfUrl == "git@github.com:camus/");
    }
}
