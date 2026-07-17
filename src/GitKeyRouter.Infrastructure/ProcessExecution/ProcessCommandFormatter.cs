using System.Text;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.ProcessExecution;

public static class ProcessCommandFormatter
{
    public static string Format(ProcessResult result) => Format(result.ExecutablePath, result.Arguments);

    public static string Format(string executablePath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(Quote(executablePath));
        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
