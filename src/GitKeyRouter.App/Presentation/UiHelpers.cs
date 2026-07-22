using System.Runtime.CompilerServices;
using System.Text;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Infrastructure.ProcessExecution;

[assembly: InternalsVisibleTo("GitKeyRouter.App.Tests")]

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
    public static Color OutputBackground { get; } = Color.FromArgb(248, 250, 253);

    public static Label CreateOverviewStatusBadge()
    {
        var badge = new Label
        {
            AutoSize = false,
            Width = 92,
            Height = 32,
            Font = new Font("Segoe UI Semibold", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = Padding.Empty
        };
        ApplyOverviewStatusBadge(badge, "未知", OverviewStatusKind.Unknown);
        return badge;
    }

    public static void ApplyOverviewStatusBadge(Label badge, string text, OverviewStatusKind statusKind)
    {
        var palette = statusKind switch
        {
            OverviewStatusKind.Normal => (Color.FromArgb(231, 246, 236), Color.FromArgb(25, 111, 61)),
            OverviewStatusKind.Warning => (Color.FromArgb(255, 247, 224), Color.FromArgb(143, 91, 0)),
            OverviewStatusKind.Error => (Color.FromArgb(253, 232, 232), Color.FromArgb(174, 38, 45)),
            OverviewStatusKind.Info => (AccentSoft, Accent),
            _ => (Color.FromArgb(240, 242, 246), TextSecondary)
        };

        badge.Text = text;
        badge.BackColor = palette.Item1;
        badge.ForeColor = palette.Item2;
    }

    internal static GridColumnWidthRange DefaultGridColumnWidthRange { get; } = new(90, 420);

    public static DataGridView CreateGrid()
        => CreateGrid(columnWidthOverrides: null);

    internal static DataGridView CreateGrid(
        IReadOnlyDictionary<string, GridColumnWidthRange>? columnWidthOverrides)
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
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ScrollBars = ScrollBars.Both,
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

        grid.DataBindingComplete += (_, _) => ApplyGridColumnLayout(grid, columnWidthOverrides);
        return grid;
    }

    internal static void ApplyGridColumnLayout(
        DataGridView grid,
        IReadOnlyDictionary<string, GridColumnWidthRange>? columnWidthOverrides = null)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ScrollBars = ScrollBars.Both;
        if (grid.Columns.Count == 0)
        {
            return;
        }

        var ranges = new Dictionary<DataGridViewColumn, GridColumnWidthRange>();
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var range = ScaleGridColumnWidthRange(
                ResolveGridColumnWidthRange(column, columnWidthOverrides),
                grid.DeviceDpi);
            ranges.Add(column, range);
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = range.MinimumWidth;
            column.Width = range.MinimumWidth;
        }

        grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var range = ranges[column];
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = range.MinimumWidth;
            column.Width = Math.Clamp(column.Width, range.MinimumWidth, range.MaximumWidth);
        }
    }

    internal static GridColumnWidthRange ScaleGridColumnWidthRange(
        GridColumnWidthRange logicalRange,
        int deviceDpi)
    {
        var effectiveDpi = deviceDpi > 0 ? deviceDpi : 96;
        var minimumWidth = Math.Max(2, logicalRange.MinimumWidth);
        var maximumWidth = Math.Max(minimumWidth, logicalRange.MaximumWidth);
        return new GridColumnWidthRange(
            ScaleGridColumnWidth(minimumWidth, effectiveDpi),
            ScaleGridColumnWidth(maximumWidth, effectiveDpi));
    }

    private static int ScaleGridColumnWidth(int logicalWidth, int deviceDpi)
        => Math.Max(
            2,
            (int)Math.Round(
                logicalWidth * (deviceDpi / 96D),
                MidpointRounding.AwayFromZero));

    private static GridColumnWidthRange ResolveGridColumnWidthRange(
        DataGridViewColumn column,
        IReadOnlyDictionary<string, GridColumnWidthRange>? columnWidthOverrides)
    {
        if (columnWidthOverrides is not null
            && (TryGetGridColumnWidthRange(columnWidthOverrides, column.Name, out var range)
                || TryGetGridColumnWidthRange(columnWidthOverrides, column.DataPropertyName, out range)
                || TryGetGridColumnWidthRange(columnWidthOverrides, column.HeaderText, out range)))
        {
            return range;
        }

        return DefaultGridColumnWidthRange;
    }

    private static bool TryGetGridColumnWidthRange(
        IReadOnlyDictionary<string, GridColumnWidthRange> columnWidthOverrides,
        string key,
        out GridColumnWidthRange range)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && columnWidthOverrides.TryGetValue(key, out range))
        {
            return true;
        }

        range = default;
        return false;
    }

    internal readonly record struct GridColumnWidthRange(int MinimumWidth, int MaximumWidth);

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

    public static void ConfigureDialog(
        Form form,
        int clientWidth,
        int clientHeight,
        bool resizable = false)
    {
        form.StartPosition = FormStartPosition.CenterParent;
        form.ClientSize = new Size(clientWidth, clientHeight);
        form.MinimumSize = resizable ? new Size(640, 420) : Size.Empty;
        form.Font = new Font("Segoe UI", 9F);
        form.BackColor = AppBackground;
        form.ForeColor = TextPrimary;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.MinimizeBox = false;
        form.MaximizeBox = resizable;
        form.ShowIcon = false;
        form.FormBorderStyle = resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
    }

    public static TableLayoutPanel CreateCompactDialogTable(
        int columnCount,
        int labelWidth,
        int? actionWidth = null)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columnCount,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Padding = new Padding(16),
            BackColor = Surface,
            Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (columnCount > 2)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, actionWidth ?? 92));
        }

        return table;
    }

    public static void AddCompactDialogRow(
        TableLayoutPanel table,
        int row,
        string labelText,
        Control editor,
        int editorColumnSpan = 1)
    {
        EnsureCompactDialogRow(table, row);
        editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        editor.Margin = new Padding(3, 4, 3, 4);
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = TextPrimary,
            Margin = new Padding(3, 4, 8, 4)
        };
        table.Controls.Add(label, 0, row);
        table.Controls.Add(editor, 1, row);
        if (editorColumnSpan > 1)
        {
            table.SetColumnSpan(editor, editorColumnSpan);
        }
    }

    public static void AddCompactDialogRow(
        TableLayoutPanel table,
        int row,
        string labelText,
        Control editor,
        Control action)
    {
        AddCompactDialogRow(table, row, labelText, editor);
        action.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        action.Margin = new Padding(5, 3, 3, 3);
        table.Controls.Add(action, 2, row);
    }

    public static void AddCompactDialogContent(
        TableLayoutPanel table,
        int row,
        Control content,
        int column,
        int columnSpan = 1)
    {
        EnsureCompactDialogRow(table, row);
        content.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        content.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(content, column, row);
        if (columnSpan > 1)
        {
            table.SetColumnSpan(content, columnSpan);
        }
    }

    public static Button CreateDialogButton(
        string text,
        DialogResult dialogResult = DialogResult.None,
        bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            DialogResult = dialogResult,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 32),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(6, 2, 0, 2),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : TextPrimary,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(36, 77, 153) : AccentSoft;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(31, 66, 132) : Color.FromArgb(214, 226, 247);
        return button;
    }

    public static FlowLayoutPanel CreateDialogButtonBar(params Button[] buttons)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12, 8, 16, 8),
            BackColor = Surface,
            Margin = Padding.Empty
        };
        bar.Controls.AddRange(buttons);
        return bar;
    }

    private static void EnsureCompactDialogRow(TableLayoutPanel table, int row)
    {
        while (table.RowCount <= row)
        {
            table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
    }

    public static Control CreatePageHeader(string title, string subtitle, string? helpText = null)
        => new ResponsivePageHeader(title, subtitle, helpText);

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

    public static TextBox CreateOutputTextBox(
        bool wordWrap = true,
        ScrollBars scrollBars = ScrollBars.Vertical)
        => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = wordWrap,
            ScrollBars = scrollBars,
            BorderStyle = BorderStyle.None,
            BackColor = OutputBackground,
            ForeColor = TextPrimary,
            Font = new Font("Consolas", 9F),
            Margin = Padding.Empty
        };

    public static Panel CreateOutputPanel(Control content)
    {
        content.Dock = DockStyle.Fill;
        content.Margin = Padding.Empty;

        var surface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = OutputBackground,
            Padding = new Padding(10)
        };
        surface.Controls.Add(content);

        var border = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Border,
            Padding = new Padding(1),
            Margin = Padding.Empty
        };
        border.Controls.Add(surface);
        return border;
    }

    public static void EnableStatusColors(DataGridView grid, params string[] columnNames)
    {
        var names = columnNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0
                || eventArgs.ColumnIndex < 0
                || eventArgs.ColumnIndex >= grid.Columns.Count
                || !names.Contains(grid.Columns[eventArgs.ColumnIndex].Name))
            {
                return;
            }

            var palette = GetStatusPalette(eventArgs.Value);
            if (palette is null)
            {
                return;
            }

            var style = eventArgs.CellStyle;
            if (style is null)
            {
                return;
            }

            style.BackColor = palette.Value.BackColor;
            style.ForeColor = palette.Value.ForeColor;
            style.SelectionBackColor = palette.Value.SelectionBackColor;
            style.SelectionForeColor = palette.Value.ForeColor;
        };
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

        foreach (var routeId in plan.RepositoryRouteIdsToRemove)
        {
            builder.Append("- config.RepositoryRoutes[").Append(routeId).AppendLine("] (legacy account-as-Owner route)");
        }

        if (!plan.HasChanges)
        {
            builder.AppendLine("No changes.");
        }

        return builder.ToString();
    }

    public static string FormatKeyRenamePlan(SshKeyRenamePlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"身份：{plan.IdentityDisplayName}");
        builder.AppendLine($"新文件名：{plan.NewBaseName}");
        builder.AppendLine();
        builder.AppendLine("文件移动：");
        foreach (var move in plan.FileMoves)
        {
            builder.AppendLine($"- {move.SourcePath}");
            builder.AppendLine($"+ {move.DestinationPath}");
        }

        builder.AppendLine();
        builder.AppendLine("将一起更新引用的身份：");
        foreach (var identityName in plan.AffectedIdentityNames)
        {
            builder.AppendLine($"- {identityName}");
        }

        builder.AppendLine();
        builder.AppendLine("SSH Config 变更：");
        builder.AppendLine(string.IsNullOrWhiteSpace(plan.SshConfigDiff) ? "无变更" : plan.SshConfigDiff);
        builder.AppendLine();
        builder.AppendLine("执行前会创建配置快照，并为每个现有密钥文件创建独立备份。失败时会尝试回滚文件与配置。");
        return builder.ToString();
    }

    private static (Color BackColor, Color ForeColor, Color SelectionBackColor)? GetStatusPalette(object? value)
    {
        if (value is bool enabled)
        {
            return enabled
                ? (Color.FromArgb(231, 246, 236), Color.FromArgb(25, 111, 61), Color.FromArgb(205, 232, 214))
                : (Color.FromArgb(240, 242, 246), TextSecondary, Color.FromArgb(222, 226, 233));
        }

        var text = Convert.ToString(value)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ContainsAny(text, "缺失", "不存在", "重复", "missing", "duplicate", "conflict", "error", "错误", "失败", "不可"))
        {
            return (Color.FromArgb(253, 232, 232), Color.FromArgb(174, 38, 45), Color.FromArgb(242, 205, 207));
        }

        if (ContainsAny(text, "未同步", "未知", "extra", "warning", "警告", "共享", "否", "未启用"))
        {
            return (Color.FromArgb(255, 247, 224), Color.FromArgb(143, 91, 0), Color.FromArgb(244, 229, 188));
        }

        if (ContainsAny(text, "正常", "存在", "correct", "normal", "success", "成功", "已同步", "独立", "是"))
        {
            return (Color.FromArgb(231, 246, 236), Color.FromArgb(25, 111, 61), Color.FromArgb(205, 232, 214));
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed class ResponsivePageHeader : Panel
    {
        private const int MinimumHeaderHeight = 72;
        private const int TitleHeight = 38;
        private const int HelpColumnWidth = 46;
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;
        private readonly Button? _helpButton;
        private bool _adjustingHeight;

        public ResponsivePageHeader(string title, string subtitle, string? helpText)
        {
            Name = "PageHeader";
            Dock = DockStyle.Top;
            Height = MinimumHeaderHeight;
            MinimumSize = new Size(0, MinimumHeaderHeight);
            BackColor = AppBackground;
            Padding = new Padding(0, 2, 0, 8);
            Margin = Padding.Empty;

            _titleLabel = new Label
            {
                Name = "PageHeaderTitle",
                Text = title,
                Font = new Font("Segoe UI Semibold", 20F),
                ForeColor = TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _subtitleLabel = new Label
            {
                Name = "PageHeaderSubtitle",
                Text = subtitle,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(_subtitleLabel);
            Controls.Add(_titleLabel);

            if (!string.IsNullOrWhiteSpace(helpText))
            {
                _helpButton = new Button
                {
                    Name = "PageHelpButton",
                    Text = "?",
                    Width = 36,
                    Height = 36,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Surface,
                    ForeColor = Accent,
                    Font = new Font("Segoe UI Semibold", 12F),
                    Cursor = Cursors.Hand,
                    AccessibleName = AppLocalization.T("页面帮助", "Page help"),
                    AccessibleDescription = AppLocalization.T("打开当前页面的使用说明", "Open instructions for the current page"),
                    UseVisualStyleBackColor = false
                };
                _helpButton.FlatAppearance.BorderColor = Border;
                _helpButton.FlatAppearance.MouseOverBackColor = AccentSoft;
                _helpButton.Click += (_, _) => MessageBox.Show(
                    FindForm() as IWin32Window ?? this,
                    helpText,
                    AppLocalization.T($"{title} - 使用帮助", $"{title} - Help"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Controls.Add(_helpButton);
            }
        }

        protected override void OnLayout(LayoutEventArgs layoutEventArgs)
        {
            if (_titleLabel is null || _subtitleLabel is null)
            {
                base.OnLayout(layoutEventArgs);
                return;
            }

            if (_adjustingHeight)
            {
                base.OnLayout(layoutEventArgs);
                return;
            }

            var helpWidth = _helpButton is null ? 0 : HelpColumnWidth;
            var contentWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal - helpWidth);
            var subtitleSize = TextRenderer.MeasureText(
                _subtitleLabel.Text,
                _subtitleLabel.Font,
                new Size(contentWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
            var targetHeight = Math.Max(
                MinimumHeaderHeight,
                Padding.Top + TitleHeight + subtitleSize.Height + Padding.Bottom);

            if (Height != targetHeight)
            {
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

            base.OnLayout(layoutEventArgs);
            _titleLabel.Bounds = new Rectangle(Padding.Left, Padding.Top, contentWidth, TitleHeight);
            _subtitleLabel.Bounds = new Rectangle(
                Padding.Left,
                Padding.Top + TitleHeight,
                contentWidth,
                Math.Max(1, ClientSize.Height - Padding.Top - TitleHeight - Padding.Bottom));
            if (_helpButton is not null)
            {
                _helpButton.Location = new Point(
                    ClientSize.Width - Padding.Right - _helpButton.Width,
                    Padding.Top + 2);
            }
        }
    }

    private sealed class WrappingToolbar : FlowLayoutPanel
    {
        private bool _adjustingHeight;

        public WrappingToolbar()
        {
            Name = "PageToolbar";
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
