using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class GitProfileEditForm : Form
{
    private readonly TextBox _displayName = new() { PlaceholderText = "例如：公司、个人" };
    private readonly TextBox _userName = new() { PlaceholderText = "Git user.name" };
    private readonly TextBox _userEmail = new() { PlaceholderText = "Git user.email" };
    private readonly TextBox _signingKey = new() { PlaceholderText = "GPG / SSH signing key，可留空" };
    private readonly CheckBox _enableSigning = new() { Text = "默认对 commit 启用签名", AutoSize = true };
    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _identity = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly IReadOnlyList<GitIdentity> _identities;
    private readonly GitProfile? _original;

    public GitProfileEditForm(IReadOnlyList<GitServiceInstance> services, IReadOnlyList<GitIdentity> identities, GitProfile? profile = null)
    {
        _identities = identities;
        _original = profile;
        Text = profile is null ? "新建 Git Profile" : "编辑 Git Profile";
        UiHelpers.ConfigureDialog(this, 680, 380);
        var serviceChoices = new List<Choice> { new(string.Empty, "<不指定>") };
        serviceChoices.AddRange(services.Select(item => new Choice(item.Id, $"{item.DisplayName} ({item.HostName})")));
        _service.DataSource = serviceChoices;
        _service.DisplayMember = nameof(Choice.DisplayText);
        _service.ValueMember = nameof(Choice.Id);
        _service.SelectedIndexChanged += (_, _) => RefreshIdentities();

        var table = UiHelpers.CreateCompactDialogTable(2, 140);
        UiHelpers.AddCompactDialogRow(table, 0, "Profile 名称", _displayName);
        UiHelpers.AddCompactDialogRow(table, 1, "user.name", _userName);
        UiHelpers.AddCompactDialogRow(table, 2, "user.email", _userEmail);
        UiHelpers.AddCompactDialogRow(table, 3, "签名密钥", _signingKey);
        UiHelpers.AddCompactDialogRow(table, 4, "Commit 签名", _enableSigning);
        UiHelpers.AddCompactDialogRow(table, 5, "默认 Git 服务", _service);
        UiHelpers.AddCompactDialogRow(table, 6, "默认 SSH 身份", _identity);
        var save = UiHelpers.CreateDialogButton("保存", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        save.Click += SaveClicked;
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
        Controls.Add(UiHelpers.CreateDialogButtonBar(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;

        if (profile is not null)
        {
            _displayName.Text = profile.DisplayName;
            _userName.Text = profile.UserName;
            _userEmail.Text = profile.UserEmail;
            _signingKey.Text = profile.SigningKey;
            _enableSigning.Checked = profile.EnableCommitSigning;
            _service.SelectedValue = profile.DefaultServiceInstanceId;
            RefreshIdentities();
            _identity.SelectedValue = profile.DefaultIdentityId;
        }
        else
        {
            _service.SelectedIndex = 0;
            RefreshIdentities();
        }
    }

    public GitProfile? ResultProfile { get; private set; }

    private void RefreshIdentities()
    {
        var selected = _identity.SelectedValue as string ?? _original?.DefaultIdentityId;
        var serviceId = _service.SelectedValue as string ?? string.Empty;
        var choices = new List<Choice> { new(string.Empty, "<不指定>") };
        choices.AddRange(_identities.Where(item => string.Equals(item.ServiceInstanceId, serviceId, StringComparison.OrdinalIgnoreCase))
            .Select(item => new Choice(item.Id, $"{item.DisplayName} ({item.AccountName} / {item.HostAlias})")));
        _identity.DataSource = choices;
        _identity.DisplayMember = nameof(Choice.DisplayText);
        _identity.ValueMember = nameof(Choice.Id);
        _identity.SelectedValue = choices.Any(item => string.Equals(item.Id, selected, StringComparison.OrdinalIgnoreCase)) ? selected : string.Empty;
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_displayName.Text) || string.IsNullOrWhiteSpace(_userName.Text) || string.IsNullOrWhiteSpace(_userEmail.Text))
        {
            MessageBox.Show(this, "Profile 名称、user.name 和 user.email 为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        ResultProfile = new GitProfile
        {
            Id = _original?.Id ?? Guid.NewGuid().ToString("N"), DisplayName = _displayName.Text.Trim(), UserName = _userName.Text.Trim(),
            UserEmail = _userEmail.Text.Trim(), SigningKey = _signingKey.Text.Trim(), EnableCommitSigning = _enableSigning.Checked,
            DefaultServiceInstanceId = _service.SelectedValue as string ?? string.Empty,
            DefaultIdentityId = _identity.SelectedValue as string ?? string.Empty, CreatedAt = _original?.CreatedAt ?? DateTimeOffset.UtcNow
        };
    }

    private sealed record Choice(string Id, string DisplayText);
}
