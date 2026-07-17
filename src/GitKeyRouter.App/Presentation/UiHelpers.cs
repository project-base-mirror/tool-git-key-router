using System.Text;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.ProcessExecution;

namespace GitKeyRouter.App.Presentation;

public static class UiHelpers
{
    public static Color AppBackground { get; } = Color.FromArgb(246, 248, 252);
    public static Color Surface { get; } = Color.White;
    public static Color SidebarBackground { get; } = Color.FromArgb(23, 32, 50);
    public static Color SidebarMuted { get; } = Color.FromArgb(157, 170, 191);
    public static Color NavigationActive { get; } = Color.FromArgb(49, 82, 141);
    public static Color Accent { get; } = Color.FromArgb(44, 91, 177);
    public static Color AccentSoft { get; } = Color.FromArgb(230, 238, 252);
    public static Color TextPrimary { get; } = Color.FromArgb(31, 41, 55);
    public static Color TextSecondary { get; } = Color.FromArgb(102, 112, 133);
    public static Color Border { get; } = Color.FromArgb(221, 226, 235);

    public static DataGridView CreateGrid()
    {
        var grid = new DataGridView
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
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EnableHeadersVisualStyles = false,
            GridColor = Border,
            ColumnHeadersHeight = 40,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 38 },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                SelectionBackColor = AccentSoft,
                SelectionForeColor = TextPrimary,
                Padding = new Padding(8, 4, 8, 4)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(250, 251, 253)
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 244, 249),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI Semibold", 9F),
                Padding = new Padding(8, 4, 8, 4),
                SelectionBackColor = Color.FromArgb(241, 244, 249),
                SelectionForeColor = TextPrimary
            }
        };

        return grid;
    }

    public static FlowLayoutPanel CreateToolbar()
        => new WrappingToolbar();

    public static Button Button(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 34),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = TextPrimary,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = AccentSoft;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 226, 247);
        button.Click += onClick;
        return button;
    }

    public static Control CreatePageHeader(string title, string subtitle)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = AppBackground,
            Padding = new Padding(0, 2, 0, 8)
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 38,
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitleLabel = new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.TopLeft
        };
        header.Controls.Add(subtitleLabel);
        header.Controls.Add(titleLabel);
        return header;
    }

    public static Panel CreateCard(Control content, Padding? padding = null)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = padding ?? new Padding(18)
        };
        card.Controls.Add(content);
        return card;
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

    private sealed class WrappingToolbar : FlowLayoutPanel
    {
        private bool _adjustingHeight;

        public WrappingToolbar()
        {
            Dock = DockStyle.Top;
            Height = 52;
            MinimumSize = new Size(0, 52);
            Padding = new Padding(0, 2, 0, 8);
            Margin = Padding.Empty;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            AutoScroll = false;
            BackColor = AppBackground;
        }

        protected override void OnLayout(LayoutEventArgs layoutEventArgs)
        {
            base.OnLayout(layoutEventArgs);
            if (_adjustingHeight || ClientSize.Width <= 0)
            {
                return;
            }

            var preferredHeight = GetPreferredSize(new Size(ClientSize.Width, 0)).Height;
            var targetHeight = Math.Max(MinimumSize.Height, preferredHeight);
            if (Height == targetHeight)
            {
                return;
            }

            _adjustingHeight = true;
            try
            {
                Height = targetHeight;
            }
            finally
            {
                _adjustingHeight = false;
            }
        }
    }
}
