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

    [Fact]
    public void Identity_AllowsSameAccountAcrossServicesButRejectsDuplicateWithinService()
    {
        var gitLab = new GitServiceInstance
        {
            Id = "gitlab-office",
            DisplayName = "Office GitLab",
            ProviderKind = GitProviderKind.GitLab,
            HostName = "gitlab.office.example",
            SshUser = "git",
            WebBaseUrl = "https://gitlab.office.example"
        };
        var existing = new GitIdentity
        {
            Id = "github-camus",
            ServiceInstanceId = GitServiceInstance.GitHubComId,
            DisplayName = "GitHub Camus",
            AccountName = "camus",
            HostAlias = "github-camus",
            PrivateKeyPath = @"C:\keys\github-camus",
            PublicKeyPath = @"C:\keys\github-camus.pub"
        };
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), gitLab],
            Identities = [existing]
        };
        var gitLabIdentity = new GitIdentity
        {
            Id = "gitlab-camus",
            ServiceInstanceId = gitLab.Id,
            DisplayName = "GitLab Camus",
            AccountName = "camus",
            HostAlias = "gitlab-camus",
            PrivateKeyPath = @"C:\keys\gitlab-camus",
            PublicKeyPath = @"C:\keys\gitlab-camus.pub"
        };

        Assert.True(IdentityValidator.Validate(gitLabIdentity, config).IsValid);

        gitLabIdentity.ServiceInstanceId = GitServiceInstance.GitHubComId;
        Assert.False(IdentityValidator.Validate(gitLabIdentity, config).IsValid);
    }

    [Fact]
    public void Identity_RejectsMissingGitService()
    {
        var identity = new GitIdentity
        {
            ServiceInstanceId = "missing-service",
            DisplayName = "Missing",
            AccountName = "camus",
            HostAlias = "missing-camus",
            PrivateKeyPath = @"C:\keys\missing",
            PublicKeyPath = @"C:\keys\missing.pub"
        };

        Assert.False(IdentityValidator.Validate(identity, new AppConfig()).IsValid);
    }

    [Fact]
    public void Identity_RejectsHostAliasMatchingAnyConfiguredServiceHostName()
    {
        var gitLab = new GitServiceInstance
        {
            Id = "gitlab-office",
            DisplayName = "Office GitLab",
            ProviderKind = GitProviderKind.GitLab,
            HostName = "gitlab.office.example",
            SshUser = "git",
            WebBaseUrl = "https://gitlab.office.example"
        };
        var identity = new GitIdentity
        {
            ServiceInstanceId = GitServiceInstance.GitHubComId,
            DisplayName = "Conflicting alias",
            AccountName = "camus",
            HostAlias = "gitlab.office.example.",
            PrivateKeyPath = @"C:\keys\conflict",
            PublicKeyPath = @"C:\keys\conflict.pub"
        };
        var config = new AppConfig
        {
            GitServices = [GitServiceInstance.CreateGitHubCom(), gitLab]
        };

        var result = IdentityValidator.Validate(identity, config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("real host name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Identity_RejectsHostAliasFromUnmanagedSshConfig()
    {
        var identity = new GitIdentity
        {
            ServiceInstanceId = GitServiceInstance.GitHubComId,
            DisplayName = "Existing SSH alias",
            AccountName = "camus",
            HostAlias = "work-git",
            PrivateKeyPath = @"C:\keys\work",
            PublicKeyPath = @"C:\keys\work.pub"
        };

        var result = IdentityValidator.Validate(identity, new AppConfig(), ["work-git"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unmanaged SSH Host", StringComparison.OrdinalIgnoreCase));
    }
}
