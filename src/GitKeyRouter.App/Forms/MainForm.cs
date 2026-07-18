using GitKeyRouter.App.Controls;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class MainForm : Form
{
    private readonly Panel _contentPanel = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(24, 20, 24, 18),
        BackColor = UiHelpers.AppBackground
    };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Dictionary<string, UserControl> _pages;
    private readonly Dictionary<string, Button> _navigationButtons = new(StringComparer.OrdinalIgnoreCase);

    public MainForm(ApplicationServices services)
    {
        Text = $"GitKeyRouter {typeof(MainForm).Assembly.GetName().Version}";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1024, 680);
        Font = new Font("Segoe UI", 9F);
        BackColor = UiHelpers.AppBackground;

        _pages = new Dictionary<string, UserControl>(StringComparer.OrdinalIgnoreCase)
        {
            ["概览"] = new OverviewControl(services, SetStatus, ShowPageAsync),
            ["GitHub 身份"] = new IdentitiesControl(services, SetStatus),
            ["Owner 路由"] = new OwnerRoutesControl(services, SetStatus),
            ["SSH Config"] = new SshConfigControl(services, SetStatus),
            ["Git 重写配置"] = new GitRewritesControl(services, SetStatus),
            ["诊断"] = new DiagnosticsControl(services, SetStatus),
            ["备份与恢复"] = new BackupControl(services, SetStatus)
        };

        var sidebar = CreateSidebar();
        var divider = new Panel
        {
            Dock = DockStyle.Left,
            Width = 1,
            BackColor = UiHelpers.Border
        };
        var statusStrip = new StatusStrip
        {
            BackColor = UiHelpers.Surface,
            ForeColor = UiHelpers.TextSecondary,
            SizingGrip = false,
            Padding = new Padding(8, 3, 8, 3)
        };
        statusStrip.Items.Add(_statusLabel);
        _statusLabel.Text = "就绪";

        Controls.Add(_contentPanel);
        Controls.Add(divider);
        Controls.Add(sidebar);
        Controls.Add(statusStrip);
        Shown += async (_, _) =>
        {
            await ShowPageAsync("概览");
            var toolsReady = await RequiredToolInstallationUi.CheckAndOfferAsync(
                this,
                services,
                SetStatus,
                showHealthyMessage: false);
            if (toolsReady)
            {
                await ShowPageAsync("概览");
            }
        };
    }

    private Panel CreateSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 232,
            BackColor = UiHelpers.SidebarBackground,
            Padding = Padding.Empty
        };

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 94,
            Padding = new Padding(18, 18, 14, 14),
            Cursor = Cursors.Hand
        };
        var mark = new Label
        {
            Text = "G",
            Dock = DockStyle.Left,
            Width = 46,
            BackColor = UiHelpers.Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 17F),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        var brandText = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 1, 0, 0),
            Cursor = Cursors.Hand
        };
        var title = new Label
        {
            Text = "GitKeyRouter",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        var subtitle = new Label
        {
            Text = "SSH 身份与路由管理",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = UiHelpers.SidebarMuted,
            TextAlign = ContentAlignment.TopLeft,
            Cursor = Cursors.Hand
        };
        brandText.Controls.Add(subtitle);
        brandText.Controls.Add(title);
        brand.Controls.Add(brandText);
        brand.Controls.Add(mark);
        foreach (var control in new Control[] { brand, mark, brandText, title, subtitle })
        {
            control.Click += async (_, _) => await ShowPageAsync("概览");
        }

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = UiHelpers.SidebarBackground,
            Padding = new Padding(12, 8, 12, 8)
        };
        var navigationLabel = new Label
        {
            Text = "导航",
            Width = 204,
            Height = 28,
            ForeColor = UiHelpers.SidebarMuted,
            Font = new Font("Segoe UI Semibold", 8F),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 6)
        };
        navigation.Controls.Add(navigationLabel);

        foreach (var pageName in _pages.Keys)
        {
            var button = CreateNavigationButton(pageName);
            _navigationButtons[pageName] = button;
            navigation.Controls.Add(button);
        }

        navigation.SizeChanged += (_, _) => ResizeNavigationItems(navigation, navigationLabel);

        var footer = new Label
        {
            Text = "点击左上角品牌可随时返回概览",
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(22, 8, 18, 10),
            ForeColor = UiHelpers.SidebarMuted,
            Font = new Font("Segoe UI", 8F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(footer);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    private Button CreateNavigationButton(string pageName)
    {
        var button = new Button
        {
            Text = pageName == "概览" ? "⌂   概览" : $"     {pageName}",
            Width = 204,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = UiHelpers.SidebarBackground,
            ForeColor = Color.FromArgb(224, 230, 240),
            Font = new Font("Segoe UI Semibold", 9.5F),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 8, 0),
            Margin = new Padding(0, 0, 0, 5),
            Cursor = Cursors.Hand,
            Tag = pageName,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 48, 72);
        button.FlatAppearance.MouseDownBackColor = UiHelpers.NavigationActive;
        button.Click += async (_, _) => await ShowPageAsync((string)button.Tag);
        return button;
    }

    private void ResizeNavigationItems(FlowLayoutPanel navigation, Label navigationLabel)
    {
        var width = Math.Max(140, navigation.ClientSize.Width - navigation.Padding.Horizontal);
        navigationLabel.Width = width;
        foreach (var button in _navigationButtons.Values)
        {
            button.Width = width;
        }
    }

    private async Task ShowPageAsync(string pageName)
    {
        if (!_pages.TryGetValue(pageName, out var page))
        {
            return;
        }

        UpdateNavigationState(pageName);
        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        page.Dock = DockStyle.Fill;
        page.BackColor = UiHelpers.AppBackground;
        _contentPanel.Controls.Add(page);
        _contentPanel.ResumeLayout();
        Text = $"GitKeyRouter - {pageName}";
        SetStatus($"正在刷新：{pageName}");
        try
        {
            if (page is IAsyncRefreshable refreshable)
            {
                await refreshable.RefreshAsync();
            }

            SetStatus($"已显示：{pageName}");
        }
        catch (Exception exception)
        {
            SetStatus($"刷新失败：{pageName}");
            MessageBox.Show(this, exception.ToString(), "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateNavigationState(string activePage)
    {
        foreach (var (pageName, button) in _navigationButtons)
        {
            var active = string.Equals(pageName, activePage, StringComparison.OrdinalIgnoreCase);
            button.BackColor = active ? UiHelpers.NavigationActive : UiHelpers.SidebarBackground;
            button.ForeColor = active ? Color.White : Color.FromArgb(224, 230, 240);
        }
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetStatus(text)));
            return;
        }

        _statusLabel.Text = text;
    }
}
