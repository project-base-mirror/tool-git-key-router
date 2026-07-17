using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("camus0109")]
    [InlineData("project-base_mirror.test")]
    public void GitHubOwner_AllowsExpectedCharacters(string owner)
        => Assert.True(GitHubOwnerValidator.Validate(owner).IsValid);

    [Theory]
    [InlineData("owner/name")]
    [InlineData("owner name")]
    [InlineData("owner:repo")]
    public void GitHubOwner_RejectsUnsafeCharacters(string owner)
        => Assert.False(GitHubOwnerValidator.Validate(owner).IsValid);

    [Fact]
    public void HostAlias_RejectsGithubDotCom()
        => Assert.False(HostAliasValidator.Validate("github.com").IsValid);

    [Fact]
    public void Identity_RejectsDuplicateHostAlias()
    {
        var existing = new GitHubIdentity
        {
            Id = "one",
            DisplayName = "One",
            GitHubUsername = "one",
            HostAlias = "github-one",
            PrivateKeyPath = @"C:\keys\one",
            PublicKeyPath = @"C:\keys\one.pub"
        };
        var candidate = new GitHubIdentity
        {
            Id = "two",
            DisplayName = "Two",
            GitHubUsername = "two",
            HostAlias = "github-one",
            PrivateKeyPath = @"C:\keys\two",
            PublicKeyPath = @"C:\keys\two.pub"
        };

        Assert.False(IdentityValidator.Validate(candidate, [existing]).IsValid);
    }
}
