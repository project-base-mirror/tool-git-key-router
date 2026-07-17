using System.Text;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Diagnostics;

public static class DiagnosticReportFormatter
{
    public static string Format(DiagnosticReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GitKeyRouter diagnostic report");
        builder.AppendLine($"Generated: {report.GeneratedAt:O}");
        builder.AppendLine($"Normal: {report.NormalCount}, Warnings: {report.WarningCount}, Errors: {report.ErrorCount}");
        builder.AppendLine(new string('-', 72));

        foreach (var item in report.Items.OrderByDescending(item => item.Severity).ThenBy(item => item.Category))
        {
            builder.Append('[').Append(item.Severity.ToString().ToUpperInvariant()).Append("] ");
            builder.Append(item.Category).Append(" / ").AppendLine(item.Title);
            builder.AppendLine(item.Message);
            if (!string.IsNullOrWhiteSpace(item.SuggestedAction))
            {
                builder.Append("Action: ").AppendLine(item.SuggestedAction);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
