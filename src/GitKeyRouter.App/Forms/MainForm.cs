using GitKeyRouter.App.Controls;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class MainForm : Form
{
    private readonly Panel _contentPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<string, UserControl> _pages;

    public MainForm(ApplicationServices services)
    {
        Text = $"GitKeyRouter {typeof(MainForm).Assembly.GetName().Version}";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1220;
        Height = 780;
        MinimumSize = new Size(980, 640);
        Font = new Font("Segoe UI", 9F);

        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 190,
            Padding = new Padding(8),
            BackColor = SystemColors.ControlLight
        };
        var title = new Label
        {
            Text = "GitKeyRouter",
            Dock = DockStyle.Top,
            Height = 56,
            Font = new Font("Segoe UI Semibold", 16F),
            TextAlign = ContentAlignment.MiddleCenter
        };
        sidebar.Controls.Add(title);

        _pages = new Dictionary<string, UserControl>(StringComparer.OrdinalIgnoreCase)
        {
            ["概览"] = new OverviewControl(services, SetStatus),
            ["GitHub 身份"] = new IdentitiesControl(services, SetStatus),
            ["Owner 路由"] = new OwnerRoutesControl(services, SetStatus),
            ["SSH Config"] = new SshConfigControl(services, SetStatus),
            ["Git 重写配置"] = new GitRewritesControl(services, SetStatus),
            ["诊断"] = new DiagnosticsControl(services, SetStatus),
            ["备份与恢复"] = new BackupControl(services, SetStatus)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        foreach (var pageName in _pages.Keys)
        {
            var button = new Button
            {
                Text = pageName,
                Width = 166,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Tag = pageName
            };
            button.Click += async (_, _) => await ShowPageAsync((string)button.Tag);
            buttonPanel.Controls.Add(button);
        }

        sidebar.Controls.Add(buttonPanel);
        sidebar.Controls.SetChildIndex(title, 0);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);
        _statusLabel.Text = "就绪";

        Controls.Add(_contentPanel);
        Controls.Add(sidebar);
        Controls.Add(statusStrip);
        Shown += async (_, _) => await ShowPageAsync("概览");
    }

    private async Task ShowPageAsync(string pageName)
    {
        if (!_pages.TryGetValue(pageName, out var page))
        {
            return;
        }

        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        page.Dock = DockStyle.Fill;
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

    private void SetStatus(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }

        _statusLabel.Text = text;
    }
}
