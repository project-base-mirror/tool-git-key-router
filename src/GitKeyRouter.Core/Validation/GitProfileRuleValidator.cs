using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class GitProfileRuleValidator
{
    public static ValidationResult Validate(GitProfileRule rule, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(config);
        var result = new ValidationResult();
        if (!config.GitProfiles.Any(item => string.Equals(item.Id, rule.ProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("The selected Git Profile does not exist.");
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            result.Add("A directory or remote URL pattern is required.");
        }
        else if (rule.Pattern.Any(char.IsControl))
        {
            result.Add("The rule pattern contains invalid control characters.");
        }
        else if (rule.Kind == GitProfileRuleKind.Directory
                 && !Path.IsPathFullyQualified(TrimDirectoryWildcard(rule.Pattern)))
        {
            result.Add("Directory rules require an absolute path.");
        }

        if (rule.Enabled && config.GitProfileRules.Any(item => item.Enabled
            && !string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase)
            && item.Kind == rule.Kind
            && string.Equals(NormalizePattern(item), NormalizePattern(rule), StringComparison.OrdinalIgnoreCase)))
        {
            result.Add("An enabled Git Profile rule already uses the same condition.");
        }

        return result;
    }

    public static string NormalizePattern(GitProfileRule rule)
        => rule.Kind == GitProfileRuleKind.Directory
            ? NormalizeDirectoryPattern(rule.Pattern)
            : rule.Pattern.Trim();

    public static string NormalizeDirectoryPattern(string value)
    {
        var path = TrimDirectoryWildcard(value);
        var fullPath = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        return fullPath + "/";
    }

    private static string TrimDirectoryWildcard(string value)
    {
        var result = value.Trim().Trim('"').Replace('\\', '/');
        while (result.EndsWith('*'))
        {
            result = result[..^1].TrimEnd('/');
        }

        return result.Replace('/', Path.DirectorySeparatorChar);
    }
}
