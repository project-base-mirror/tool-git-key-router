using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class GitServiceEditForm : Form
{
    private readonly ComboBox _template = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _displayName = new();
    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _hostName = new();
    private readonly NumericUpDown _sshPort = new() { Minimum = 1, Maximum = 65535, Value = 22 };
    private readonly TextBox _sshUser = new() { Text = "git" };
    private readonly TextBox _webBaseUrl = new();
    private readonly CheckBox _extendedSshUrls = new() { Text = "生成 ssh:// 与 git+ssh:// rewrite", AutoSize = true };
    private readonly CheckBox _allowInsecureHttp = new() { Text = "同时接受 HTTP URL（不安全）", AutoSize = true };
    private readonly ComboBox _defaultIdentity = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _defaultIdentityHint = new()
    {
        AutoSize = true,
        MaximumSize = new Size(470, 0),
        Text = "GitHub 默认身份是兜底规则：未配置 Owner 或仓库路由的所有 GitHub 仓库都会使用此身份；更具体的路由优先。",
        ForeColor = Color.FromArgb(143, 91, 0),
        BackColor = Color.FromArgb(255, 247, 224),
        Padding = new Padding(8),
        Visible = false
    };
    private readonly GitServiceInstance? _original;
    private string _suggestedId = string.Empty;

    public GitServiceEditForm(GitServiceInstance? service = null, IReadOnlyList<GitIdentity>? identities = null)
    {
        _original = service;
        Text = service is null ? "新建 Git 服务" : "编辑 Git 服务";
        UiHelpers.ConfigureDialog(this, 680, 500);

        _template.Items.AddRange(GitServiceService.AvailableTemplates.Cast<object>().ToArray());
        _provider.Items.AddRange(Enum.GetValues<GitProviderKind>().Cast<object>().ToArray());
        _defaultIdentity.DisplayMember = nameof(IdentityChoice.DisplayText);
        _defaultIdentity.Items.Add(new IdentityChoice(null, "<无默认身份>"));
        _defaultIdentity.Items.AddRange((identities ?? [])
            .Where(item => service is null || string.Equals(item.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            .Select(item => new IdentityChoice(item.Id, $"{item.DisplayName} ({item.AccountName} / {item.HostAlias})"))
            .Cast<object>()
            .ToArray());
        _defaultIdentity.SelectedIndex = 0;
        _template.SelectedIndexChanged += (_, _) => ApplyTemplate();
        _provider.SelectedIndexChanged += (_, _) => UpdateDefaultIdentityHint();
        _defaultIdentity.SelectedIndexChanged += (_, _) => UpdateDefaultIdentityHint();

        var table = UiHelpers.CreateCompactDialogTable(2, 140);
        UiHelpers.AddCompactDialogRow(table, 0, "快速模板", _template);
        UiHelpers.AddCompactDialogRow(table, 1, "显示名称", _displayName);
        UiHelpers.AddCompactDialogRow(table, 2, "服务类型", _provider);
        UiHelpers.AddCompactDialogRow(table, 3, "域名 / 主机名", _hostName);
        UiHelpers.AddCompactDialogRow(table, 4, "SSH 端口", _sshPort);
        UiHelpers.AddCompactDialogRow(table, 5, "SSH 用户", _sshUser);
        UiHelpers.AddCompactDialogRow(table, 6, "Web Base URL", _webBaseUrl);
        UiHelpers.AddCompactDialogRow(table, 7, "默认身份", _defaultIdentity);
        UiHelpers.AddCompactDialogContent(table, 8, _defaultIdentityHint, 1);
        UiHelpers.AddCompactDialogRow(table, 9, "扩展 URL", _extendedSshUrls);
        UiHelpers.AddCompactDialogRow(table, 10, "HTTP 兼容", _allowInsecureHttp);

        var save = UiHelpers.CreateDialogButton("保存", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        save.Click += SaveClicked;
        var buttons = UiHelpers.CreateDialogButtonBar(save, cancel);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
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
                _allowInsecureHttp.Enabled = false;
            }
        }
    }

    public GitServiceInstance? ResultService { get; private set; }


    private void ApplyTemplate()
    {
        if (_template.SelectedItem is not string template)
        {
            return;
        }

        var service = GitServiceService.CreateTemplate(template);
        _suggestedId = service.Id;
        LoadService(service);
    }

    private void LoadService(GitServiceInstance service)
    {
        _displayName.Text = service.DisplayName;
        _provider.SelectedItem = service.ProviderKind;
        _hostName.Text = service.HostName;
        _sshPort.Value = Math.Clamp(
            service.SshPort ?? 0,
            decimal.ToInt32(_sshPort.Minimum),
            decimal.ToInt32(_sshPort.Maximum));
        _sshUser.Text = service.SshUser;
        _webBaseUrl.Text = service.WebBaseUrl;
        _extendedSshUrls.Checked = service.EnableExtendedSshUrlRewrites;
        _allowInsecureHttp.Checked = service.AllowInsecureHttp;
        _defaultIdentity.SelectedIndex = _defaultIdentity.Items.Cast<IdentityChoice>().ToList().FindIndex(item =>
            string.Equals(item.Id, service.DefaultIdentityId, StringComparison.OrdinalIgnoreCase));
        if (_defaultIdentity.SelectedIndex < 0)
        {
            _defaultIdentity.SelectedIndex = 0;
        }

        UpdateDefaultIdentityHint();
    }

    private void UpdateDefaultIdentityHint()
        => _defaultIdentityHint.Visible = _provider.SelectedItem is GitProviderKind.GitHub
            && (_defaultIdentity.SelectedItem as IdentityChoice)?.Id is not null;

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
            Id = _original?.Id ?? _suggestedId,
            DisplayName = _displayName.Text.Trim(),
            ProviderKind = _provider.SelectedItem is GitProviderKind kind ? kind : GitProviderKind.Generic,
            HostName = _hostName.Text.Trim(),
            SshPort = (int)_sshPort.Value,
            SshUser = _sshUser.Text.Trim(),
            WebBaseUrl = _webBaseUrl.Text.Trim(),
            EnableExtendedSshUrlRewrites = _extendedSshUrls.Checked,
            AllowInsecureHttp = _allowInsecureHttp.Checked,
            DefaultIdentityId = (_defaultIdentity.SelectedItem as IdentityChoice)?.Id,
            IsBuiltIn = _original?.IsBuiltIn == true
        };
    }

    private sealed record IdentityChoice(string? Id, string DisplayText);
}
