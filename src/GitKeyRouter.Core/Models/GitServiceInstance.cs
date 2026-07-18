namespace GitKeyRouter.Core.Models;

public sealed class GitServiceInstance
{
    public const string GitHubComId = "github.com";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public GitProviderKind ProviderKind { get; set; } = GitProviderKind.Generic;

    public string HostName { get; set; } = string.Empty;

    public int? SshPort { get; set; }

    public string SshUser { get; set; } = "git";

    public string WebBaseUrl { get; set; } = string.Empty;

    public bool AllowInsecureHttp { get; set; }

    public bool EnableExtendedSshUrlRewrites { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public static GitServiceInstance CreateGitHubCom()
        => new()
        {
            Id = GitHubComId,
            DisplayName = "GitHub.com",
            ProviderKind = GitProviderKind.GitHub,
            HostName = "github.com",
            SshUser = "git",
            WebBaseUrl = "https://github.com",
            EnableExtendedSshUrlRewrites = false,
            IsBuiltIn = true
        };
}
