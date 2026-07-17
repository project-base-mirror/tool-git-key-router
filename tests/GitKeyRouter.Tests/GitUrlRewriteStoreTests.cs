using GitKeyRouter.Infrastructure.Git;

namespace GitKeyRouter.Tests;

public sealed class GitUrlRewriteStoreTests
{
    [Fact]
    public void Parse_ReadsGitConfigOutput()
    {
        var output = "url.git@github-camus:camus0109/.insteadof https://github.com/camus0109/\n"
            + "url.git@github-camus:camus0109/.insteadof git@github.com:camus0109/\n";

        var rules = GitUrlRewriteStore.Parse(output);

        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule => Assert.Equal("git@github-camus:camus0109/", rule.BaseUrl));
    }
}
