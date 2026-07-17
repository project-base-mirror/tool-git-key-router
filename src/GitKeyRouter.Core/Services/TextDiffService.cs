using System.Text;

namespace GitKeyRouter.Core.Services;

public static class TextDiffService
{
    public static string CreateSimpleDiff(string original, string updated, string oldName, string newName)
    {
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            return "No changes.";
        }

        var oldLines = SplitLines(original);
        var newLines = SplitLines(updated);
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length
            && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var oldSuffix = oldLines.Length - 1;
        var newSuffix = newLines.Length - 1;
        while (oldSuffix >= prefix && newSuffix >= prefix
            && string.Equals(oldLines[oldSuffix], newLines[newSuffix], StringComparison.Ordinal))
        {
            oldSuffix--;
            newSuffix--;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"--- {oldName}");
        builder.AppendLine($"+++ {newName}");
        builder.AppendLine($"@@ -{prefix + 1},{Math.Max(0, oldSuffix - prefix + 1)} +{prefix + 1},{Math.Max(0, newSuffix - prefix + 1)} @@");

        for (var i = prefix; i <= oldSuffix; i++)
        {
            builder.Append('-').AppendLine(oldLines[i]);
        }

        for (var i = prefix; i <= newSuffix; i++)
        {
            builder.Append('+').AppendLine(newLines[i]);
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
}
