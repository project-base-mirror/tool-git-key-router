using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public static class AppConfigMigrator
{
    public static AppConfig FromSchema1(
        IEnumerable<Schema1GitHubIdentity> identities,
        IEnumerable<Schema1OwnerRoute> ownerRoutes)
    {
        var config = new AppConfig
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            GitServices = [GitServiceInstance.CreateGitHubCom()],
            Identities = identities.Select(identity => new GitIdentity
            {
                Id = identity.Id,
                ServiceInstanceId = GitServiceInstance.GitHubComId,
                DisplayName = identity.DisplayName,
                AccountName = identity.GitHubUsername,
                HostAlias = identity.HostAlias,
                PrivateKeyPath = identity.PrivateKeyPath,
                PublicKeyPath = identity.PublicKeyPath,
                EmailOrComment = identity.EmailOrComment,
                CreatedAt = identity.CreatedAt
            }).ToList(),
            RepositoryRoutes = ownerRoutes.Select(route => new RepositoryRoute
            {
                ServiceInstanceId = GitServiceInstance.GitHubComId,
                NamespacePath = route.GitHubOwner,
                IdentityId = route.IdentityId,
                Enabled = route.Enabled
            }).ToList()
        };
        config.Normalize();
        return config;
    }

    public sealed class Schema1GitHubIdentity
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public string GitHubUsername { get; set; } = string.Empty;
        public string HostAlias { get; set; } = string.Empty;
        public string PrivateKeyPath { get; set; } = string.Empty;
        public string PublicKeyPath { get; set; } = string.Empty;
        public string EmailOrComment { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class Schema1OwnerRoute
    {
        public string GitHubOwner { get; set; } = string.Empty;
        public string IdentityId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }
}
