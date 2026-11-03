using System.Text.Json;
using System.Text.Json.Serialization;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.Infrastructure.Logging;

namespace GitKeyRouter.App.Cli;

public sealed partial class CliApplication
{
    private string _command = string.Empty;
    private bool _json;

    private static readonly JsonSerializerOptions RecordJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new RedactedStringConverter() }
    };

    private static bool IsManagementCommand(string command)
        => command is "rename-service" or "save-service" or "save-identity" or "save-route" or "save-profile" or "save-profile-rule"
            or "delete-service" or "delete-identity" or "delete-route" or "delete-profile" or "delete-profile-rule";

    public static bool WantsJson(string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        return args.Take(separator < 0 ? args.Length : separator)
            .Contains("--json", StringComparer.OrdinalIgnoreCase);
    }

    public static int ReportError(string[] args, int exitCode, string code, string message)
    {
        if (WantsJson(args))
        {
            WriteJsonResponse(NormalizeCommand(args), null, exitCode, new { code, message });
        }
        else
        {
            Console.Error.WriteLine(SensitiveDataRedactor.Redact(message));
        }

        return exitCode;
    }

    private int WriteJson(object? data, int exitCode = 0, object? error = null)
    {
        WriteJsonResponse(_command, data, exitCode, error);
        return exitCode;
    }

    private static void WriteJsonResponse(string command, object? data, int exitCode, object? error)
        => Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, command, exitCode, data, error }, OutputJsonOptions));

    // Validate before any service call. In particular, a typo next to --yes must never write.
    public static string? ValidateArguments(string[] args)
    {
        var command = NormalizeCommand(args);
        if (command is "gh" or "gh-login" or "gh-logout" or "gh-status" or "gh-resolve"
            or "help" or "--help" or "-h" or "version" or "--version" or "-v")
        {
            return null; // These commands retain their existing argument/output contracts.
        }

        var (positionals, switches, values) = command switch
        {
            "diagnose" or "list-services" or "list-identities" or "list-profiles" or "list-routes"
                or "list-profile-rules" or "list-templates" => (0, "--json", ""),
            "rename-service" => (2, "--json --yes", ""),
            "parse-url" => (1, "--json", ""),
            "resolve-profile" => (1, "--json", "--url"),
            "test-route" => (1, "--json --connect", "--service --url"),
            "apply" or "apply-profiles" => (0, "--json --yes", ""),
            "test-service" => (1, "", ""),
            "test-ssh" => (1, "--verbose", ""),
            "ssh-backend" => (0, "--use-openssh --yes", ""),
            "trust-host" => (1, "--yes", ""),
            _ when IsManagementCommand(command) && command.StartsWith("save-", StringComparison.Ordinal)
                => (0, "--json --yes", "--file"),
            _ when IsManagementCommand(command) => (1, "--json --yes", ""),
            _ => (-1, "", "")
        };
        if (positionals < 0)
        {
            return "Unknown command. Run help for supported commands.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suppliedPositionals = 0;
        var foundOption = false;
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (foundOption || string.IsNullOrWhiteSpace(argument))
                {
                    return "Put positional arguments before options; unexpected or empty argument.";
                }

                suppliedPositionals++;
                continue;
            }

            // --json may appear before the positional argument as well.
            foundOption |= !string.Equals(argument, "--json", StringComparison.OrdinalIgnoreCase);
            if (!seen.Add(argument))
            {
                return "Duplicate option. Run help for usage.";
            }

            if (switches.Split(' ').Contains(argument, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!values.Split(' ').Contains(argument, StringComparer.OrdinalIgnoreCase)
                || ++index >= args.Length || string.IsNullOrWhiteSpace(args[index])
                || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                return "Unknown option or missing option value. Run help for usage.";
            }
        }

        if (suppliedPositionals != positionals)
        {
            return "Incorrect number of positional arguments. Run help for usage.";
        }

        if (IsManagementCommand(command)
            && (!seen.Contains("--yes") || command.StartsWith("save-", StringComparison.Ordinal) && !seen.Contains("--file")))
        {
            return "Record changes require --yes; save commands also require --file <record.json|->. Review the record before confirming.";
        }

        if (command == "test-route" && seen.Contains("--connect") && seen.Contains("--json"))
        {
            return "test-route --json supports offline preview only; omit --json for an explicit --connect test.";
        }

        return null;
    }

    private int ListTemplates()
    {
        var templates = GitServiceService.AvailableTemplates.Select(name => new
        {
            name,
            service = GitServiceService.CreateTemplate(name)
        }).ToList();
        if (_json)
        {
            return WriteJson(templates);
        }

        foreach (var template in templates)
        {
            Console.WriteLine($"{template.name}\t{template.service.ProviderKind}\t{template.service.HostName}");
        }

        return 0;
    }

    private async Task<int> ListProfileRulesAsync(CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_json)
        {
            return WriteJson(config.GitProfileRules);
        }

        foreach (var rule in config.GitProfileRules)
        {
            Console.WriteLine($"{rule.Id}\t{rule.ProfileId}\t{rule.Kind}\t{rule.Pattern}\tEnabled={rule.Enabled}");
        }

        return 0;
    }

    private async Task<int> ManageAsync(string command, string[] args, CancellationToken cancellationToken)
    {
        OperationResult result = command switch
        {
            "rename-service" => await _services.GitServiceService.RenameAsync(args[0], args[1], cancellationToken),
            "save-service" => await SaveServiceAsync(await ReadRecordAsync<GitServiceInstance>(args, cancellationToken), cancellationToken),
            "save-identity" => await _services.IdentityService.SaveAsync(await ReadRecordAsync<GitIdentity>(args, cancellationToken), cancellationToken),
            "save-route" => await _services.OwnerRouteService.SaveAsync(await ReadRecordAsync<RepositoryRoute>(args, cancellationToken), cancellationToken: cancellationToken),
            "save-profile" => await _services.GitProfileService.SaveProfileAsync(await ReadRecordAsync<GitProfile>(args, cancellationToken), cancellationToken),
            "save-profile-rule" => await _services.GitProfileService.SaveRuleAsync(await ReadRecordAsync<GitProfileRule>(args, cancellationToken), cancellationToken),
            "delete-service" => await _services.GitServiceService.DeleteAsync(args[0], cancellationToken),
            "delete-identity" => await _services.IdentityService.DeleteAsync(args[0], cancellationToken),
            "delete-route" => await _services.OwnerRouteService.DeleteByIdAsync(args[0], cancellationToken),
            "delete-profile" => await _services.GitProfileService.DeleteProfileAsync(args[0], cancellationToken),
            "delete-profile-rule" => await _services.GitProfileService.DeleteRuleAsync(args[0], cancellationToken),
            _ => throw new InvalidOperationException("Unknown management command.")
        };
        var exitCode = result.Success ? 0 : 2;
        if (_json)
        {
            return WriteJson(result, exitCode, result.Success ? null : new { code = "validation_failed", message = result.Message, details = result.Errors });
        }

        if (!result.Success)
        {
            PrintErrors(result);
            return exitCode;
        }

        Console.WriteLine(result.Message);
        return exitCode;
    }

    private async Task<OperationResult<GitServiceInstance>> SaveServiceAsync(GitServiceInstance service, CancellationToken cancellationToken)
    {
        var config = await _services.ConfigStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (service.IsBuiltIn && config.FindService(service.Id)?.IsBuiltIn != true)
        {
            return OperationResult<GitServiceInstance>.Fail("Only the existing built-in service may have IsBuiltIn=true.");
        }

        return await _services.GitServiceService.SaveAsync(service, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadRecordAsync<T>(string[] args, CancellationToken cancellationToken)
    {
        var path = GetOption(args, "--file")!;
        var json = path == "-"
            ? await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
            : await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a record.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? id = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Duplicate property.");
            }

            if (property.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                id = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || id.Any(char.IsControl))
        {
            throw new JsonException("An explicit stable Id is required.");
        }

        return root.Deserialize<T>(RecordJsonOptions) ?? throw new JsonException("Expected a record.");
    }

    private sealed class RedactedStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            => writer.WriteStringValue(SensitiveDataRedactor.Redact(value));
    }
}
