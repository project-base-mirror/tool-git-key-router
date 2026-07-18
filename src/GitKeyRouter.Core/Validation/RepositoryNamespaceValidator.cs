using System.Text.RegularExpressions;

namespace GitKeyRouter.Core.Validation;

public static partial class RepositoryNamespaceValidator
{
    public static ValidationResult Validate(string? value)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Add("Repository namespace is required.");
            return result;
        }

        var normalized = value.Trim('/');
        if (normalized.Length > 240 || !NamespacePattern().IsMatch(normalized))
        {
            result.Add("Repository namespace must contain slash-separated letters, numbers, dots, underscores, or hyphens.");
        }

        return result;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePattern();
}
