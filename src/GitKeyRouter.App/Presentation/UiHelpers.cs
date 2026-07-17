using System.Text;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.ProcessExecution;

namespace GitKeyRouter.App.Presentation;

public static class UiHelpers
{
    public static DataGridView CreateGrid()
        => new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle
        };

    public static FlowLayoutPanel CreateToolbar()
        => new()
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true
        };

    public static Button Button(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28 };
        button.Click += onClick;
        return button;
    }

    public static void ShowErrors(IWin32Window owner, OperationResult result)
    {
        var text = result.Message;
        if (result.Errors.Count > 0)
        {
            text += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Errors);
        }

        MessageBox.Show(owner, text, "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static string FormatProcess(ProcessResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {ProcessCommandFormatter.Format(result)}");
        builder.AppendLine($"Exit code: {result.ExitCode?.ToString() ?? "<none>"}");
        builder.AppendLine($"Timed out: {result.TimedOut}");
        builder.AppendLine($"Cancelled: {result.Cancelled}");
        builder.AppendLine($"Duration: {result.Duration}");
        builder.AppendLine();
        builder.AppendLine("stdout:");
        builder.AppendLine(result.StandardOutput);
        builder.AppendLine();
        builder.AppendLine("stderr:");
        builder.AppendLine(result.StandardError);
        return builder.ToString();
    }

    public static string FormatGitPlan(GitRewritePlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Git URL rewrite change plan");
        builder.AppendLine();
        foreach (var rule in plan.Removes)
        {
            builder.Append("- ").Append(rule.ConfigKey).Append(" = ").AppendLine(rule.InsteadOfUrl);
        }

        foreach (var rule in plan.Adds)
        {
            builder.Append("+ ").Append(rule.ConfigKey).Append(" = ").AppendLine(rule.InsteadOfUrl);
        }

        if (!plan.HasChanges)
        {
            builder.AppendLine("No changes.");
        }

        return builder.ToString();
    }
}
