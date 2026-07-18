using GitKeyRouter.Core.Diagnostics;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Cli;

public sealed class CliApplication
{
    private readonly ApplicationServices _services;

    public CliApplication(ApplicationServices services)
    {
        _services = services;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var command = args[0].Trim().ToLowerInvariant();
        return command switch
        {
            "diagnose" => await DiagnoseAsync(cancellationToken).ConfigureAwait(false),
            "list-identities" => await ListIdentitiesAsync(cancellationToken).ConfigureAwait(false),
            "list-routes" => await ListRoutesAsync(cancellationToken).ConfigureAwait(false),
            "apply" => await ApplyAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "test-route" => await TestRouteAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "test-ssh" => await TestSshAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "version" or "--version" or "-v" => PrintVersion(),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => UnknownCommand(command)
        };
    }

    private async Task<int> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var report = await _services.DiagnosticService.RunAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine(DiagnosticReportFormatter.Format(report));
        return report.ErrorCount > 0 ? 2 : report.WarningCount > 0 ? 1 : 0;
    }

    private async Task<int> ListIdentitiesAsync(CancellationToken cancellationToken)
    {
        var identities = await _services.IdentityService.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var identity in identities)
        {
            Console.WriteLine($"{identity.Id}\t{identity.DisplayName}\t{identity.GitHubUsername}\t{identity.HostAlias}\t{identity.PrivateKeyPath}");
        }

        return 0;
    }

    private async Task<int> ListRoutesAsync(CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var route in config.OwnerRoutes.OrderBy(item => item.GitHubOwner, StringComparer.OrdinalIgnoreCase))
        {
            var identity = config.Identities.FirstOrDefault(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"{route.GitHubOwner}\t{identity?.HostAlias ?? "<missing identity>"}\tEnabled={route.Enabled}");
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

        Console.WriteLine("SSH config changes:");
        Console.WriteLine(sshPreview.DiffText);
        Console.WriteLine();
        Console.WriteLine(FormatGitPlan(gitPlan));

        if (!confirmed)
        {
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

        Console.WriteLine("Configuration applied successfully.");
        return 0;
    }

    private async Task<int> TestRouteAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe test-route <owner> [--url <repository-url>] [--connect]");
            return 3;
        }

        var owner = args[0];
        var url = GetOption(args, "--url") ?? $"https://github.com/{owner}/__route_preview__.git";
        var preview = await _services.GitUrlRewriteService.PreviewAsync(url, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Original:  {preview.OriginalUrl}");
        Console.WriteLine($"Matched:   {preview.MatchedPrefix ?? "<none>"}");
        Console.WriteLine($"Base URL:  {preview.MatchedBaseUrl ?? "<none>"}");
        Console.WriteLine($"Rewritten: {preview.RewrittenUrl}");

        if (!args.Contains("--connect", StringComparer.OrdinalIgnoreCase))
        {
            return preview.WasRewritten ? 0 : 1;
        }

        if (GetOption(args, "--url") is null)
        {
            Console.Error.WriteLine("--connect requires an explicit --url so GitKeyRouter does not contact a fabricated repository.");
            return 3;
        }

        var result = await _services.GitUrlRewriteService.TestRemoteAsync(url, cancellationToken).ConfigureAwait(false);
        PrintProcess(result);
        return result.Succeeded ? 0 : 2;
    }

    private async Task<int> TestSshAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GitKeyRouter.exe test-ssh <host-alias> [--verbose]");
            return 3;
        }

        var result = await _services.SshKeyService.TestAsync(
            args[0],
            args.Contains("--verbose", StringComparer.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Classification: {result.Classification}");
        PrintProcess(result.Process);
        return result.Authenticated ? 0 : 2;
    }

    private static int PrintVersion()
    {
        Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("GitKeyRouter commands:");
        Console.WriteLine("  diagnose");
        Console.WriteLine("  list-identities");
        Console.WriteLine("  list-routes");
        Console.WriteLine("  apply [--yes]");
        Console.WriteLine("  test-route <owner> [--url <repository-url>] [--connect]");
        Console.WriteLine("  test-ssh <host-alias> [--verbose]");
        Console.WriteLine("  version");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 3;
    }

    private static string? GetOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, item => string.Equals(item, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FormatGitPlan(GitRewritePlan plan)
    {
        var lines = new List<string> { "Git URL rewrite changes:" };
        lines.AddRange(plan.Removes.Select(rule => $"- {rule.ConfigKey} = {rule.InsteadOfUrl}"));
        lines.AddRange(plan.Adds.Select(rule => $"+ {rule.ConfigKey} = {rule.InsteadOfUrl}"));
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
        Console.WriteLine("stdout:");
        Console.WriteLine(result.StandardOutput);
        Console.WriteLine("stderr:");
        Console.WriteLine(result.StandardError);
    }

    private static void PrintErrors(OperationResult result)
    {
        Console.Error.WriteLine(result.Message);
        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine(error);
        }
    }
}
