using System.Text.Json;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Validation;

namespace GitKeyRouter.Core.Services;

public sealed class GitServiceService
{
    private readonly IAppConfigStore _configStore;
    private readonly IBackupService _backupService;
    private readonly IProcessRunner _processRunner;
    private readonly IToolchainService _toolchainService;
    private readonly GitProviderAdapterRegistry _providers;

    public GitServiceService(
        IAppConfigStore configStore,
        IBackupService backupService,
        IProcessRunner processRunner,
        IToolchainService toolchainService,
        GitProviderAdapterRegistry providers)
    {
        _configStore = configStore;
        _backupService = backupService;
        _processRunner = processRunner;
        _toolchainService = toolchainService;
        _providers = providers;
    }

    public async Task<IReadOnlyList<GitServiceInstance>> ListAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _configStore.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var config = snapshot.Config;
        return config.GitServices.OrderBy(item => item.IsBuiltIn ? 0 : 1)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Task<OperationResult<GitServiceInstance>> SaveAsync(
        GitServiceInstance service,
        CancellationToken cancellationToken = default)
        => SaveAsync(service, originalId: null, cancellationToken);

    public async Task<OperationResult<GitServiceInstance>> SaveAsync(
        GitServiceInstance service,
        string? originalId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _configStore.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return await SaveAsync(service, originalId, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<GitServiceInstance>> RenameAsync(
        string id,
        string newId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _configStore.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var existing = snapshot.Config.FindService(id);
        if (existing is null)
        {
            return OperationResult<GitServiceInstance>.Fail("Git service does not exist.");
        }

        var service = JsonSerializer.Deserialize<GitServiceInstance>(JsonSerializer.Serialize(existing))!;
        service.Id = newId;
        return await SaveAsync(service, id, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<GitServiceInstance>> SaveAsync(
        GitServiceInstance service,
        string? originalId,
        AppConfigSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        // Validate a detached candidate so a rejected rename cannot mutate the loaded snapshot.
        var config = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(snapshot.Config))!;
        var existing = config.FindService(originalId ?? service.Id);
        if (originalId is not null && existing is null)
        {
            return OperationResult<GitServiceInstance>.Fail("The original Git service no longer exists. Reload and retry.");
        }

        if (originalId is not null && string.IsNullOrWhiteSpace(service.Id))
        {
            return OperationResult<GitServiceInstance>.Fail("A new service ID is required.");
        }

        var newId = NormalizeId(service.Id, service.DisplayName, service.HostName);
        var renamed = existing is not null && !string.Equals(existing.Id, newId, StringComparison.OrdinalIgnoreCase);
        if (renamed && existing!.IsBuiltIn)
        {
            return OperationResult<GitServiceInstance>.Fail("The built-in GitHub.com service ID cannot be changed.");
        }

        if ((existing is null || renamed)
            && (newId.Length > 128 || !newId.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            return OperationResult<GitServiceInstance>.Fail("Service ID must use letters, digits, dots, hyphens or underscores (up to 128 characters).");
        }

        if (renamed && config.FindService(newId) is not null)
        {
            return OperationResult<GitServiceInstance>.Fail("The new service ID is already in use.");
        }

        if (renamed && config.RepositoryRoutes.Any(route =>
                string.Equals(route.Id, $"service-default:{newId}", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(route.ServiceInstanceId, existing!.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<GitServiceInstance>.Fail("The new service ID conflicts with an existing managed route ID.");
        }

        var existingIndex = existing is null ? -1 : config.GitServices.IndexOf(existing);
        if (existing?.IsBuiltIn == true)
        {
            service.Id = existing.Id;
            service.ProviderKind = existing.ProviderKind;
            service.HostName = existing.HostName;
            service.SshUser = existing.SshUser;
            service.SshPort = existing.SshPort;
            service.WebBaseUrl = existing.WebBaseUrl;
            service.AllowInsecureHttp = existing.AllowInsecureHttp;
            service.EnableExtendedSshUrlRewrites = existing.EnableExtendedSshUrlRewrites;
            service.IsBuiltIn = true;
        }

        service.Id = newId;
        if (renamed)
        {
            RenameReferences(config, existing!.Id, newId);
            config.GitServices[existingIndex] = service;
        }

        service.HostName = service.HostName.Trim();
        service.SshUser = service.SshUser.Trim();
        service.WebBaseUrl = service.WebBaseUrl.TrimEnd('/');
        service.SshPort ??= 22;
        var validation = GitServiceValidator.Validate(service, config);
        if (!validation.IsValid)
        {
            return OperationResult<GitServiceInstance>.Fail("Git service validation failed.", validation.Errors.ToArray());
        }

        await _backupService.CreateSnapshotAsync(renamed
            ? $"Rename Git service: {existing!.Id} -> {service.Id}"
            : $"Save Git service: {service.DisplayName}", cancellationToken).ConfigureAwait(false);
        if (existingIndex >= 0)
        {
            config.GitServices[existingIndex] = service;
        }
        else
        {
            config.GitServices.Add(service);
        }

        config.SynchronizeDefaultServiceRoutes();

        await _configStore.SaveIfUnchangedAsync(config, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return OperationResult<GitServiceInstance>.Ok(service, renamed
            ? "Git service saved; identity, route and profile references were updated."
            : "Git service saved.");
    }

    private static void RenameReferences(AppConfig config, string oldId, string newId)
    {
        foreach (var identity in config.Identities.Where(item => string.Equals(item.ServiceInstanceId, oldId, StringComparison.OrdinalIgnoreCase)))
        {
            identity.ServiceInstanceId = newId;
        }

        foreach (var route in config.RepositoryRoutes.Where(item => string.Equals(item.ServiceInstanceId, oldId, StringComparison.OrdinalIgnoreCase)))
        {
            route.ServiceInstanceId = newId;
            if (string.Equals(route.Id, $"service-default:{oldId}", StringComparison.OrdinalIgnoreCase))
            {
                route.Id = $"service-default:{newId}";
            }
        }

        foreach (var profile in config.GitProfiles.Where(item => string.Equals(item.DefaultServiceInstanceId, oldId, StringComparison.OrdinalIgnoreCase)))
        {
            profile.DefaultServiceInstanceId = newId;
        }
    }

    public async Task<OperationResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var snapshot = await _configStore.LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var config = snapshot.Config;
        var service = config.FindService(id);
        if (service is null)
        {
            return OperationResult.Fail("Git service does not exist.");
        }

        if (service.IsBuiltIn)
        {
            return OperationResult.Fail("The built-in GitHub.com service cannot be deleted.");
        }

        if (config.Identities.Any(item => string.Equals(item.ServiceInstanceId, id, StringComparison.OrdinalIgnoreCase))
            || config.RepositoryRoutes.Any(item => string.Equals(item.ServiceInstanceId, id, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult.Fail("The Git service is still referenced by identities or repository routes.");
        }

        await _backupService.CreateSnapshotAsync($"Delete Git service: {service.DisplayName}", cancellationToken).ConfigureAwait(false);
        config.GitServices.Remove(service);
        await _configStore.SaveIfUnchangedAsync(config, snapshot.Version, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok("Git service deleted.");
    }

    public async Task<OperationResult<GitServiceConnectionResult>> TestConnectionAsync(
        GitServiceInstance service,
        CancellationToken cancellationToken = default)
    {
        var validation = GitServiceValidator.Validate(service, []);
        if (!validation.IsValid)
        {
            return OperationResult<GitServiceConnectionResult>.Fail("Git service validation failed.", validation.Errors.ToArray());
        }

        var tools = await _toolchainService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.Ssh.Exists || string.IsNullOrWhiteSpace(tools.Ssh.SelectedPath))
        {
            return OperationResult<GitServiceConnectionResult>.Fail("ssh.exe was not found.");
        }

        var arguments = new List<string> { "-T", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10" };
        if (service.SshPort is > 0 and not 22)
        {
            arguments.AddRange(["-p", service.SshPort.Value.ToString()]);
        }

        arguments.Add($"{service.SshUser}@{service.HostName}");
        var process = await _processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = tools.Ssh.SelectedPath,
            Arguments = arguments,
            Timeout = TimeSpan.FromSeconds(20)
        }, cancellationToken).ConfigureAwait(false);
        var adapter = _providers.Get(service.ProviderKind);
        var authenticated = adapter.IsAuthenticationSuccess(process);
        var output = process.StandardOutput + "\n" + process.StandardError;
        var classification = authenticated
            ? $"{service.ProviderKind} authentication succeeded"
            : process.TimedOut
                ? "Connection timed out"
                : output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                    ? "SSH endpoint reached; key authentication was rejected"
                    : output.Contains("Could not resolve hostname", StringComparison.OrdinalIgnoreCase)
                        ? "DNS resolution failed"
                        : SshHostTrustService.IsHostKeyVerificationFailure(process)
                            ? "SSH server host key is not trusted or conflicts with known_hosts"
                            : "SSH connection did not confirm authentication";

        var result = new GitServiceConnectionResult
        {
            Service = service,
            Process = process,
            Authenticated = authenticated,
            Classification = classification
        };
        return OperationResult<GitServiceConnectionResult>.Ok(result, classification);
    }

    public static IReadOnlyList<string> AvailableTemplates { get; } =
    [
        "Gitea Local",
        "Gitea Cloud",
        "GitLab.com",
        "Gitee",
        "自建 GitLab",
        "自建 Gitea",
        "Gogs",
        "OneDev",
        "GitBucket",
        "通用 Git 服务"
    ];

    public static GitServiceInstance CreateTemplate(string template)
        => template switch
        {
            "Gitee" => new GitServiceInstance
            {
                Id = "gitee.com",
                DisplayName = "Gitee",
                ProviderKind = GitProviderKind.Gitee,
                HostName = "gitee.com",
                SshPort = 22,
                SshUser = "git",
                WebBaseUrl = "https://gitee.com"
            },
            "GitLab.com" => new GitServiceInstance
            {
                Id = "gitlab.com",
                DisplayName = "GitLab.com",
                ProviderKind = GitProviderKind.GitLab,
                HostName = "gitlab.com",
                SshUser = "git",
                WebBaseUrl = "https://gitlab.com"
            },
            "自建 GitLab" => new GitServiceInstance { Id = string.Empty, DisplayName = "自建 GitLab", ProviderKind = GitProviderKind.GitLab, SshUser = "git" },
            "Gitea Local" => new GitServiceInstance
            {
                Id = "gitea-local",
                DisplayName = "Gitea Local",
                ProviderKind = GitProviderKind.Gitea,
                HostName = "gitea.lan.policoil.top",
                SshPort = 22,
                SshUser = "git",
                WebBaseUrl = "https://gitea.lan.policoil.top"
            },
            "Gitea Cloud" => new GitServiceInstance
            {
                Id = "gitea-cloud",
                DisplayName = "Gitea Cloud",
                ProviderKind = GitProviderKind.Gitea,
                HostName = "git.policoil.top",
                SshPort = 22,
                SshUser = "git",
                WebBaseUrl = "https://git.policoil.top"
            },
            "自建 Gitea" => new GitServiceInstance { Id = string.Empty, DisplayName = "自建 Gitea", ProviderKind = GitProviderKind.Gitea, SshPort = 22, SshUser = "git" },
            "Gogs" => CreateSelfHostedGenericTemplate("Gogs"),
            "OneDev" => CreateSelfHostedGenericTemplate("OneDev"),
            "GitBucket" => CreateSelfHostedGenericTemplate("GitBucket"),
            _ => new GitServiceInstance { Id = string.Empty, DisplayName = "自定义 Git 服务", ProviderKind = GitProviderKind.Generic, SshUser = "git" }
        };

    private static GitServiceInstance CreateSelfHostedGenericTemplate(string displayName)
        => new()
        {
            Id = string.Empty,
            DisplayName = displayName,
            ProviderKind = GitProviderKind.Generic,
            SshPort = 22,
            SshUser = "git"
        };

    private static string NormalizeId(string id, string displayName, string hostName)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id.Trim().ToLowerInvariant();
        }

        var source = string.IsNullOrWhiteSpace(hostName) ? displayName : hostName;
        var normalized = new string(source.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("N") : normalized;
    }

}
