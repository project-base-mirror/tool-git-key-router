using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Forms;

public sealed class GitServiceEditForm : Form
{
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _displayName = new();
    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hostName = new();
    private readonly NumericUpDown _sshPort = new() { Minimum = 0, Maximum = 65535, Value = 0 };
    private readonly TextBox _sshUser = new() { Text = "git" };
    private readonly TextBox _webBaseUrl = new();
    private readonly GitServiceInstance? _original;

    public GitServiceEditForm(GitServiceInstance? service = null)
    {
        _original = service;
        Text = service is null ? "新建 Git 服务" : "编辑 Git 服务";
        StartPosition = FormStartPosition.CenterParent;
        Width = 680;
        Height = 420;
        MinimizeBox = false;
        MaximizeBox = false;

        _template.Items.AddRange(["GitLab.com", "自建 GitLab", "自建 Gitea", "通用 Git 服务"]);
        _provider.DataSource = Enum.GetValues<GitProviderKind>();
        _template.SelectedIndexChanged += (_, _) => ApplyTemplate();

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < table.RowCount; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / table.RowCount));
        }

        AddRow(table, 0, "快速模板", _template);
        AddRow(table, 1, "显示名称", _displayName);
        AddRow(table, 2, "服务类型", _provider);
        AddRow(table, 3, "域名 / 主机名", _hostName);
        AddRow(table, 4, "SSH 端口（0=默认）", _sshPort);
        AddRow(table, 5, "SSH 用户", _sshUser);
        AddRow(table, 6, "Web Base URL", _webBaseUrl);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var save = new Button { Text = "保存", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += SaveClicked;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;

        if (service is null)
        {
            _template.SelectedIndex = 0;
        }
        else
        {
            LoadService(service);
            _template.Enabled = false;
            if (service.IsBuiltIn)
            {
                _provider.Enabled = false;
                _hostName.ReadOnly = true;
                _sshPort.Enabled = false;
                _sshUser.ReadOnly = true;
                _webBaseUrl.ReadOnly = true;
            }
        }
    }

    public GitServiceInstance? ResultService { get; private set; }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control editor)
    {
        editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        editor.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 4, 3, 4)
        }, 0, row);
        table.Controls.Add(editor, 1, row);
    }

    private void ApplyTemplate()
    {
        if (_template.SelectedItem is not string template)
        {
            return;
        }

        LoadService(GitServiceService.CreateTemplate(template));
    }

    private void LoadService(GitServiceInstance service)
    {
        _displayName.Text = service.DisplayName;
        _provider.SelectedItem = service.ProviderKind;
        _hostName.Text = service.HostName;
        _sshPort.Value = service.SshPort ?? 0;
        _sshUser.Text = service.SshUser;
        _webBaseUrl.Text = service.WebBaseUrl;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_displayName.Text)
            || string.IsNullOrWhiteSpace(_hostName.Text)
            || string.IsNullOrWhiteSpace(_sshUser.Text)
            || string.IsNullOrWhiteSpace(_webBaseUrl.Text))
        {
            MessageBox.Show(this, "显示名称、主机名、SSH 用户和 Web Base URL 为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultService = new GitServiceInstance
        {
            Id = _original?.Id ?? string.Empty,
            DisplayName = _displayName.Text.Trim(),
            ProviderKind = _provider.SelectedItem is GitProviderKind kind ? kind : GitProviderKind.Generic,
            HostName = _hostName.Text.Trim(),
            SshPort = _sshPort.Value == 0 ? null : (int)_sshPort.Value,
            SshUser = _sshUser.Text.Trim(),
            WebBaseUrl = _webBaseUrl.Text.Trim(),
            IsBuiltIn = _original?.IsBuiltIn == true
        };
    }
}
