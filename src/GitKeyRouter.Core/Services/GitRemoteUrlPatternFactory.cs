using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Services;

public static class GitRemoteUrlPatternFactory
{
    public static IReadOnlyList<GitRemoteUrlPattern> Create(GitServiceInstance service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var patterns = new List<GitRemoteUrlPattern>();
        if (Uri.TryCreate(service.WebBaseUrl, UriKind.Absolute, out var webUri)
            && webUri.Scheme is "http" or "https")
        {
            var webPrefix = service.WebBaseUrl.TrimEnd('/') + "/";
            patterns.Add(new GitRemoteUrlPattern(
                GitRemoteUrlPatternKind.Web,
                webPrefix,
                IsInsecureHttp: webUri.Scheme == "http",
                IncludesPort: !webUri.IsDefaultPort,
                HasWebSubPath: webUri.AbsolutePath.Trim('/').Length > 0));

            if (service.AllowInsecureHttp && webUri.Scheme == "https")
            {
                var builder = new UriBuilder(webUri)
                {
                    Scheme = "http",
                    Port = webUri.IsDefaultPort ? -1 : webUri.Port
                };
                patterns.Add(new GitRemoteUrlPattern(
                    GitRemoteUrlPatternKind.Web,
                    builder.Uri.ToString().TrimEnd('/') + "/",
                    IsInsecureHttp: true,
                    IncludesPort: !builder.Uri.IsDefaultPort,
                    HasWebSubPath: builder.Uri.AbsolutePath.Trim('/').Length > 0));
            }
        }

        patterns.Add(new GitRemoteUrlPattern(
            GitRemoteUrlPatternKind.Scp,
            $"{service.SshUser}@{service.HostName}:"));

        var portPart = service.SshPort is > 0 and not 22 ? $":{service.SshPort.Value}" : string.Empty;
        patterns.Add(new GitRemoteUrlPattern(
            GitRemoteUrlPatternKind.Ssh,
            $"ssh://{service.SshUser}@{service.HostName}{portPart}/",
            IncludesPort: portPart.Length > 0));
        patterns.Add(new GitRemoteUrlPattern(
            GitRemoteUrlPatternKind.GitSsh,
            $"git+ssh://{service.SshUser}@{service.HostName}{portPart}/",
            IncludesPort: portPart.Length > 0));

        return patterns
            .GroupBy(item => item.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }
}
