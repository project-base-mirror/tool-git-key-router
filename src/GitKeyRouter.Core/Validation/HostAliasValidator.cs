using System.Text.RegularExpressions;

namespace GitKeyRouter.Core.Validation;

public static partial class HostAliasValidator
{
    [GeneratedRegex("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();

    public static ValidationResult Validate(string? alias)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(alias))
        {
            result.Add("HostAlias is required.");
            return result;
        }

        if (alias.Length > 100)
        {
            result.Add("HostAlias must not exceed 100 characters.");
        }

        if (!AllowedPattern().IsMatch(alias))
        {
            result.Add("HostAlias may contain only letters, digits, underscore, dot, and hyphen.");
        }

        if (string.Equals(alias, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("HostAlias must not replace the standard github.com host.");
        }

        return result;
    }
}
