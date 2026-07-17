using System.Text.RegularExpressions;

namespace GitKeyRouter.Core.Validation;

public static partial class GitHubOwnerValidator
{
    [GeneratedRegex("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPattern();

    public static ValidationResult Validate(string? owner)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(owner))
        {
            result.Add("GitHub Owner is required.");
            return result;
        }

        if (owner.Length > 100)
        {
            result.Add("GitHub Owner must not exceed 100 characters.");
        }

        if (!AllowedPattern().IsMatch(owner))
        {
            result.Add("GitHub Owner may contain only letters, digits, underscore, dot, and hyphen.");
        }

        return result;
    }
}
