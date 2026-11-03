using GitKeyRouter.Core.Diagnostics;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Cli;

public sealed partial class CliApplication
{
    private readonly ApplicationServices _services;

    public CliApplication(ApplicationServices services)
    {
        _services = services;
    }

    public static bool CanRunWithoutServices(string[] args)
        => args.Length > 0 && NormalizeCommand(args) is "version" or "--version" or "-v" or "help" or "--help" or "-h";

    public static int RunWithoutServices(string[] args)
        => NormalizeCommand(args) switch
        {
            "version" or "--version" or "-v" => PrintVersion(),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => throw new ArgumentException("The command requires application services.", nameof(args))
        };

    public static bool RequiresExclusiveInstance(string[] args)
    {
        if (args.Length == 0)
        {
            return true;
        }

        var command = NormalizeCommand(args);
        return (command is "apply" or "apply-profiles" or "ssh-backend" or "trust-host"
            || IsManagementCommand(command))
            && args.Skip(1).Contains("--yes", StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        _command = NormalizeCommand(args);
        _json = WantsJson(args);
        try
        {
            var validationError = ValidateArguments(args);
            if (validationError is not null)
            {
                return ReportError(args, 3, "invalid_arguments", validationError);
            }

            var commandArgs = _command.StartsWith("gh", StringComparison.Ordinal)
                ? args
                : args.Where(argument => !string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();
            return await RunCommandAsync(commandArgs, cancellationToken).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return ReportError(args, 3, "invalid_json", "Expected a JSON record with known fields, valid types and string enum names. Input was not saved.");
        }
        catch (AppConfigConcurrencyException exception)
        {
            return ReportError(args, 4, "config_conflict", exception.Message);
        }
        catch (OperationCanceledException)
        {
            return ReportError(args, 2, "cancelled", "Operation cancelled; inspect the current configuration before retrying.");
        }
        catch (Exception exception)
        {
            return ReportError(args, 2, "operation_failed", exception.Message);
        }
    }

    private async Task<int> RunCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = NormalizeCommand(args);
        if (IsManagementCommand(command))
        {
            return await ManageAsync(command, args[1..], cancellationToken).ConfigureAwait(false);
        }

        return command switch
        {
            "list-templates" => ListTemplates(),
            "list-profile-rules" => await ListProfileRulesAsync(cancellationToken).ConfigureAwait(false),
            "diagnose" => await DiagnoseAsync(cancellationToken).ConfigureAwait(false),
            "list-services" => await ListServicesAsync(cancellationToken).ConfigureAwait(false),
            "list-identities" => await ListIdentitiesAsync(cancellationToken).ConfigureAwait(false),
            "list-profiles" => await ListProfilesAsync(cancellationToken).ConfigureAwait(false),
            "list-routes" => await ListRoutesAsync(cancellationToken).ConfigureAwait(false),
            "apply" => await ApplyAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "apply-profiles" => await ApplyProfilesAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "parse-url" => await ParseUrlAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "resolve-profile" => await ResolveProfileAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "test-service" => await TestServiceAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "test-route" => await TestRouteAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "test-ssh" => await TestSshAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "ssh-backend" => await SshBackendAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "trust-host" => await TrustHostAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "gh-login" => await GitHubLoginAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "gh-logout" => await GitHubLogoutAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "gh-status" => await GitHubStatusAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "gh-resolve" => await GitHubResolveAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "gh" => await GitHubAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "version" or "--version" or "-v" => PrintVersion(),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => UnknownCommand(command)
        };
    }

    private async Task<int> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var report = await _services.DiagnosticService.RunAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(report, report.ErrorCount > 0 ? 2 : report.WarningCount > 0 ? 1 : 0);
        }

        Console.WriteLine(DiagnosticReportFormatter.Format(report));
        return report.ErrorCount > 0 ? 2 : report.WarningCount > 0 ? 1 : 0;
    }

    private async Task<int> ListServicesAsync(CancellationToken cancellationToken)
    {
        var services = await _services.GitServiceService.ListAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(services);
        }

        foreach (var service in services)
        {
            Console.WriteLine($"{service.Id}\t{service.DisplayName}\t{service.ProviderKind}\t{service.SshUser}@{service.HostName}:{service.SshPort ?? 22}\t{service.WebBaseUrl}\tBuiltIn={service.IsBuiltIn}");
        }

        return 0;
    }

    private async Task<int> ListIdentitiesAsync(CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(config.Identities);
        }

        foreach (var identity in config.Identities.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var service = config.FindService(identity.ServiceInstanceId);
            Console.WriteLine($"{identity.Id}\t{service?.DisplayName ?? identity.ServiceInstanceId}\t{identity.DisplayName}\t{identity.AccountName}\t{identity.HostAlias}\t{identity.PrivateKeyPath}");
        }

        return 0;
    }

    private async Task<int> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(config.GitProfiles);
        }

        foreach (var profile in config.GitProfiles.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var ruleCount = config.GitProfileRules.Count(item =>
                string.Equals(item.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine(
                $"{profile.Id}\t{profile.DisplayName}\t{profile.UserName}\t{profile.UserEmail}\tSigning={profile.EnableCommitSigning}\tRules={ruleCount}");
        }

        return 0;
    }

    private async Task<int> ListRoutesAsync(CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(config.RepositoryRoutes);
        }

        foreach (var route in config.RepositoryRoutes
                     .OrderBy(item => item.ServiceInstanceId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.NamespacePath, StringComparer.OrdinalIgnoreCase))
        {
            var identity = config.Identities.FirstOrDefault(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            var service = config.FindService(route.ServiceInstanceId);
            Console.WriteLine($"{service?.DisplayName ?? route.ServiceInstanceId}\t{route.NamespacePath}\t{identity?.HostAlias ?? "<missing identity>"}\tEnabled={route.Enabled}");
        }

        return 0;
    }

    private async Task<int> ApplyAsync(string[] args, CancellationToken cancellationToken)
    {
        var confirmed = args.Contains("--yes", StringComparer.OrdinalIgnoreCase);
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var raw = await _services.SshConfigService.ReadRawAsync(cancellationToken).ConfigureAwait(false);
        var sshPreview = _services.SshConfigService.PreviewSynchronizeAll(raw, config);
        var gitPlan = await _services.GitUrlRewriteService.BuildReconcilePlanAsync(cancellationToken).ConfigureAwait(false);

        if (!_json)
        {
            Console.WriteLine("SSH config changes:");
            Console.WriteLine(sshPreview.DiffText);
            Console.WriteLine();
            Console.WriteLine(FormatGitPlan(gitPlan));
        }

        if (!confirmed)
        {
            if (_json)
            {
                return WriteJson(new { applied = false, ssh = sshPreview, git = gitPlan },
                    sshPreview.HasChanges || gitPlan.HasChanges ? 1 : 0);
            }

            Console.WriteLine("Preview only. Re-run with --yes to apply these changes.");
            return sshPreview.HasChanges || gitPlan.HasChanges ? 1 : 0;
        }

        if (sshPreview.HasChanges)
        {
            var sshResult = await _services.SshConfigService.ApplyAsync(sshPreview, "CLI apply: synchronize SSH config", cancellationToken).ConfigureAwait(false);
            if (!sshResult.Success)
            {
                PrintErrors(sshResult);
                return 2;
            }
        }

        var gitResult = await _services.GitUrlRewriteService.ApplyPlanAsync(gitPlan, "CLI apply: reconcile Git URL rewrites", cancellationToken).ConfigureAwait(false);
        if (!gitResult.Success)
        {
            PrintErrors(gitResult);
            return 2;
        }

        if (_json)
        {
            return WriteJson(new { applied = true, ssh = sshPreview, git = gitPlan });
        }

        Console.WriteLine("Configuration applied successfully.");
        return 0;
    }

    private async Task<int> ApplyProfilesAsync(string[] args, CancellationToken cancellationToken)
    {
        var preview = await _services.GitProfileService.BuildPreviewAsync(cancellationToken).ConfigureAwait(false);
        if (!_json)
        {
            Console.WriteLine(preview.DiffText);
        }

        if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase))
        {
            if (_json)
            {
                return WriteJson(new { applied = false, preview }, preview.HasChanges ? 1 : 0);
            }

            Console.Error.WriteLine("Preview only. Re-run with --yes to apply Git Profile conditional config.");
            return preview.HasChanges ? 2 : 0;
        }

        var result = await _services.GitProfileService.ApplyAsync(preview, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            PrintErrors(result);
            return 2;
        }

        if (_json)
        {
            return WriteJson(new { applied = true, result.Value });
        }

        Console.WriteLine($"Applied {result.Value.ProfileFileCount} profile files.");
        Console.WriteLine($"Master include: {result.Value.MasterConfigPath}");
        return 0;
    }

    private async Task<int> ResolveProfileAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe resolve-profile <repository-directory> [--url <remote-url>]");
            return 3;
        }

        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var remoteUrl = GetOption(args, "--url");
        var profile = _services.GitProfileService.ResolveProfile(
            config,
            args[0],
            remoteUrl is null ? [] : [remoteUrl]);
        if (_json)
        {
            return WriteJson(profile, profile is null ? 1 : 0);
        }

        if (profile is null)
        {
            Console.Error.WriteLine("No Git Profile rule matched the repository context.");
            return 1;
        }

        Console.WriteLine($"Profile: {profile.DisplayName} ({profile.Id})");
        Console.WriteLine($"user.name: {profile.UserName}");
        Console.WriteLine($"user.email: {profile.UserEmail}");
        Console.WriteLine($"Signing: {profile.EnableCommitSigning}");
        return 0;
    }

    private async Task<int> TestRouteAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe test-route <namespace> [--service <id-or-host>] [--url <repository-url>] [--connect]");
            return 3;
        }

        var namespacePath = args[0].Trim('/');
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var service = ResolveService(config, GetOption(args, "--service"));
        if (service is null)
        {
            return ReportError(_json ? [_command, "--json"] : [_command], 3, "not_found", "The selected Git service was not found.");
        }

        var url = GetOption(args, "--url")
            ?? $"{service.WebBaseUrl.TrimEnd('/')}/{namespacePath}/__route_preview__.git";
        var preview = await _services.GitUrlRewriteService.PreviewAsync(url, cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(preview, preview.WasRewritten ? 0 : 1);
        }

        Console.WriteLine($"Original:          {preview.OriginalUrl}");
        Console.WriteLine($"Actual matched:    {preview.ActualMatchedPrefix ?? "<none>"}");
        Console.WriteLine($"Actual base URL:   {preview.ActualMatchedBaseUrl ?? "<none>"}");
        Console.WriteLine($"Actual rewritten:  {preview.ActualRewrittenUrl ?? preview.OriginalUrl}");
        Console.WriteLine($"Expected matched:  {preview.ExpectedMatchedPrefix ?? "<none>"}");
        Console.WriteLine($"Expected base URL: {preview.ExpectedMatchedBaseUrl ?? "<none>"}");
        Console.WriteLine($"Expected rewrite:  {preview.ExpectedRewrittenUrl ?? preview.RewrittenUrl}");
        Console.WriteLine($"Expected status:   {preview.ExpectationStatus}");

        if (!args.Contains("--connect", StringComparer.OrdinalIgnoreCase))
        {
            return preview.WasRewritten ? 0 : 1;
        }

        if (GetOption(args, "--url") is null)
        {
            Console.Error.WriteLine("--connect requires an explicit --url so GitKeyRouter does not contact a fabricated repository.");
            return 3;
        }

        var backend = await _services.GitSshBackendService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!backend.Success || backend.Value is null)
        {
            PrintErrors(backend);
            return 2;
        }

        PrintSshBackend(backend.Value);
        if (!backend.Value.IsOpenSsh)
        {
            Console.Error.WriteLine(
                "Connection was not attempted because GitKeyRouter routing requires Git to use OpenSSH. " +
                "Run 'ssh-backend --use-openssh' to preview the repair.");
            return 2;
        }

        var result = await _services.GitUrlRewriteService.TestRemoteRouteAsync(url, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Connection: {result.Classification}");
        Console.WriteLine($"Password fallback: {result.PasswordFallbackDetected}");
        PrintProcess(result.Process);
        if (SshHostTrustService.IsHostKeyVerificationFailure(result.Process))
        {
            Console.Error.WriteLine($"Preview the server fingerprints with: GitKeyRouter.exe trust-host {service.Id}");
        }

        return result.AuthenticationSucceeded ? 0 : 2;
    }

    private async Task<int> ParseUrlAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe parse-url <repository-url>");
            return 3;
        }

        var result = await _services.GitUrlRewriteService.ParseRemoteUrlAsync(args[0], cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(result, result is null ? 1 : 0);
        }

        if (result is null)
        {
            Console.Error.WriteLine("The repository URL does not match any configured Git service.");
            return 1;
        }

        Console.WriteLine($"Service:    {result.ServiceDisplayName} ({result.ServiceInstanceId})");
        Console.WriteLine($"Format:     {result.PatternKind}");
        Console.WriteLine($"Namespace:  {result.NamespacePath}");
        Console.WriteLine($"Repository: {result.RepositoryName}");
        Console.WriteLine($"Prefix:     {result.MatchedPrefix}");
        return 0;
    }

    private async Task<int> TestSshAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe test-ssh <host-alias> [--verbose]");
            return 3;
        }

        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var identity = config.Identities.FirstOrDefault(item =>
            string.Equals(item.HostAlias, args[0], StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Id, args[0], StringComparison.OrdinalIgnoreCase));
        var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);
        SshTestResult result;
        if (identity is null)
        {
            result = await _services.SshKeyService.TestAsync(args[0], verbose, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var service = config.FindService(identity.ServiceInstanceId);
            if (service is null)
            {
                Console.Error.WriteLine($"Git service '{identity.ServiceInstanceId}' was not found.");
                return 2;
            }

            result = await _services.SshKeyService.TestAsync(service, identity, verbose, cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine($"Classification: {result.Classification}");
        PrintProcess(result.Process);
        return result.Authenticated ? 0 : 2;
    }

    private async Task<int> TestServiceAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe test-service <id-or-host>");
            return 3;
        }

        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var service = ResolveService(config, args[0]);
        if (service is null)
        {
            return ReportError(_json ? [_command, "--json"] : [_command], 3, "not_found", "The selected Git service was not found.");
        }

        var result = await _services.GitServiceService.TestConnectionAsync(service, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            PrintErrors(result);
            return 2;
        }

        Console.WriteLine($"Classification: {result.Value.Classification}");
        PrintProcess(result.Value.Process);
        return result.Value.Authenticated ? 0 : 2;
    }

    private async Task<int> SshBackendAsync(string[] args, CancellationToken cancellationToken)
    {
        var inspectionResult = await _services.GitSshBackendService
            .InspectAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!inspectionResult.Success || inspectionResult.Value is null)
        {
            PrintErrors(inspectionResult);
            return 2;
        }

        var inspection = inspectionResult.Value;
        PrintSshBackend(inspection);
        var fixRequested = args.Contains("--use-openssh", StringComparer.OrdinalIgnoreCase);
        if (!fixRequested)
        {
            return inspection.IsOpenSsh ? 0 : 1;
        }

        if (inspection.IsOpenSsh)
        {
            Console.WriteLine("Git already uses OpenSSH; no setting was changed.");
            return 0;
        }

        if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Preview only. Re-run with --use-openssh --yes to write the global Git settings.");
            return inspection.IsOpenSsh ? 0 : 1;
        }

        var apply = await _services.GitSshBackendService
            .UseOpenSshAsync(inspection, cancellationToken)
            .ConfigureAwait(false);
        if (!apply.Success || apply.Value is null)
        {
            PrintErrors(apply);
            return 2;
        }

        Console.WriteLine("Git now uses OpenSSH.");
        PrintSshBackend(apply.Value.After);
        return 0;
    }

    private async Task<int> TrustHostAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe trust-host <id-or-host> [--yes]");
            return 3;
        }

        var backend = await _services.GitSshBackendService.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!backend.Success || backend.Value is null)
        {
            PrintErrors(backend);
            return 2;
        }

        if (!backend.Value.IsOpenSsh)
        {
            PrintSshBackend(backend.Value);
            Console.Error.WriteLine(
                "Host trust was not changed because Git does not use OpenSSH. " +
                "Run 'ssh-backend --use-openssh' first.");
            return 2;
        }

        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var service = ResolveService(config, args[0]);
        if (service is null)
        {
            return ReportError(_json ? [_command, "--json"] : [_command], 3, "not_found", "The selected Git service was not found.");
        }

        var previewResult = await _services.SshHostTrustService
            .BuildPreviewAsync(service, cancellationToken)
            .ConfigureAwait(false);
        if (!previewResult.Success || previewResult.Value is null)
        {
            PrintErrors(previewResult);
            return 2;
        }

        var preview = previewResult.Value;
        Console.WriteLine($"Service:     {preview.ServiceDisplayName}");
        Console.WriteLine($"Endpoint:    {preview.HostIdentifier}");
        Console.WriteLine($"known_hosts: {preview.KnownHostsPath}");
        Console.WriteLine($"Status:      {preview.Status}");
        Console.WriteLine("Scanned server fingerprints:");
        foreach (var key in preview.ScannedKeys)
        {
            Console.WriteLine($"  {key.KeyType}\t{key.Fingerprint}");
        }

        if (preview.Status == SshHostTrustStatus.Conflict)
        {
            Console.Error.WriteLine("Existing known_hosts entries conflict with the scan; no automatic change is allowed.");
            return 2;
        }

        if (preview.Status == SshHostTrustStatus.Trusted)
        {
            Console.WriteLine("The scanned host keys are already trusted.");
            return 0;
        }

        if (!args.Contains("--yes", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Preview only. Verify every SHA-256 fingerprint through a trusted channel, then re-run with --yes.");
            return 1;
        }

        var apply = await _services.SshHostTrustService
            .TrustAsync(service, preview, cancellationToken)
            .ConfigureAwait(false);
        if (!apply.Success || apply.Value is null)
        {
            PrintErrors(apply);
            return 2;
        }

        Console.WriteLine($"Trusted {apply.Value.AddedKeyCount} host key(s).");
        if (!string.IsNullOrWhiteSpace(apply.Value.BackupPath))
        {
            Console.WriteLine($"Backup: {apply.Value.BackupPath}");
        }

        return 0;
    }

    private static void PrintSshBackend(GitSshBackendInspection inspection)
    {
        Console.WriteLine($"Backend:          {inspection.DisplayName}");
        Console.WriteLine($"Source:           {inspection.Source}");
        Console.WriteLine($"Executable:       {inspection.EffectiveExecutable ?? "<Git default>"}");
        Console.WriteLine($"Variant:          {inspection.EffectiveVariant ?? "<automatic>"}");
        Console.WriteLine($"Detected OpenSSH: {inspection.SelectedOpenSshPath ?? "<not found>"}");
        foreach (var blocker in inspection.EnvironmentBlockers)
        {
            Console.WriteLine($"Environment blocker: {blocker}");
        }
    }

    private static int PrintVersion()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("GitKeyRouter commands:");
        Console.WriteLine("  list-templates [--json]");
        Console.WriteLine("  rename-service <current-id> <new-id> --yes [--json] (updates all app references)");
        Console.WriteLine("  list-profile-rules [--json]");
        Console.WriteLine("  save-service|save-identity|save-route|save-profile|save-profile-rule --file <record.json|-> --yes [--json]");
        Console.WriteLine("  delete-service|delete-identity|delete-route|delete-profile|delete-profile-rule <id> --yes [--json]");
        Console.WriteLine("  Saves replace records by explicit Id; deletes use exact Id. These edit app records; use apply/apply-profiles separately.");
        Console.WriteLine("  --json: supported for diagnose, list-*, parse-url, resolve-profile, test-route (preview), apply and apply-profiles.");
        Console.WriteLine("  JSON envelope: schemaVersion, command, exitCode, data, error. Exit 0=success, 1=preview/no match/warnings, 2=failure, 3=invalid input, 4=busy/conflict.");
        Console.WriteLine("  diagnose");
        Console.WriteLine("  list-services");
        Console.WriteLine("  list-identities");
        Console.WriteLine("  list-profiles");
        Console.WriteLine("  list-routes");
        Console.WriteLine("  apply [--yes]");
        Console.WriteLine("  apply-profiles [--yes]");
        Console.WriteLine("  parse-url <repository-url>");
        Console.WriteLine("  resolve-profile <repository-directory> [--url <remote-url>]");
        Console.WriteLine("  test-service <id-or-host>");
        Console.WriteLine("  test-route <namespace> [--service <id-or-host>] [--url <repository-url>] [--connect]");
        Console.WriteLine("  test-ssh <host-alias-or-identity-id> [--verbose]");
        Console.WriteLine("  ssh-backend [--use-openssh --yes]");
        Console.WriteLine("  trust-host <id-or-host> [--yes]");
        Console.WriteLine("  gh-login <identity-id-or-host-alias>");
        Console.WriteLine("  gh-logout <identity-id-or-host-alias> --yes");
        Console.WriteLine("  gh-status [identity-id-or-host-alias] [--json]");
        Console.WriteLine("  gh-status --all [--json]");
        Console.WriteLine("  gh-resolve [--identity <identity>] [-R|--repo <host/owner/repo>] [--json]");
        Console.WriteLine("  gh [--identity <identity-id-or-host-alias>] -- <gh-arguments>");
        Console.WriteLine("  version");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 3;
    }

    private static string NormalizeCommand(string[] args)
        => args.Length == 0 ? string.Empty : args[0].Trim().ToLowerInvariant();

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static GitServiceInstance? ResolveService(AppConfig config, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return config.FindService(GitServiceInstance.GitHubComId)
                ?? config.GitServices.FirstOrDefault();
        }

        return config.GitServices.FirstOrDefault(item =>
            string.Equals(item.Id, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.HostName, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.DisplayName, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatGitPlan(GitRewritePlan plan)
    {
        var lines = new List<string> { "Git URL rewrite changes:" };
        lines.AddRange(plan.Removes.Select(rule => $"- {rule.ConfigKey} = {rule.InsteadOfUrl}"));
        lines.AddRange(plan.Adds.Select(rule => $"+ {rule.ConfigKey} = {rule.InsteadOfUrl}"));
        lines.AddRange(plan.RepositoryRouteIdsToRemove.Select(routeId => $"- config.RepositoryRoutes[{routeId}] (legacy account-as-Owner route)"));
        if (!plan.HasChanges)
        {
            lines.Add("No changes.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void PrintProcess(ProcessResult result)
    {
        Console.WriteLine($"Executable: {result.ExecutablePath}");
        Console.WriteLine($"Arguments:  {string.Join(" | ", result.Arguments)}");
        Console.WriteLine($"Exit code:  {result.ExitCode?.ToString() ?? "<none>"}");
        Console.WriteLine($"Timed out:  {result.TimedOut}");
        Console.WriteLine($"Cancelled:  {result.Cancelled}");
        Console.WriteLine($"Kill failed: {result.KillFailed}");
        Console.WriteLine($"stdout truncated: {result.StandardOutputTruncated}");
        Console.WriteLine($"stderr truncated: {result.StandardErrorTruncated}");
        if (!string.IsNullOrWhiteSpace(result.TerminationError))
        {
            Console.WriteLine($"Termination error: {result.TerminationError}");
        }

        Console.WriteLine("stdout:");
        Console.WriteLine(result.StandardOutput);
        Console.WriteLine("stderr:");
        Console.WriteLine(result.StandardError);
    }

    private void PrintErrors(OperationResult result)
    {
        if (_json)
        {
            WriteJson(null, 2, new { code = "operation_failed", message = result.Message, details = result.Errors });
            return;
        }

        Console.Error.WriteLine(result.Message);
        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine(error);
        }
    }
}
