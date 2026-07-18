using GitKeyRouter.Core.Models;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class OwnerRouteEditForm : Form
{
    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _namespace = new() { PlaceholderText = "例如：openai、team/platform 或组织路径" };
    private readonly ComboBox _identity = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _enabled = new() { Text = "启用", Checked = true, AutoSize = true };
    private readonly RepositoryRoute? _original;
    private readonly IReadOnlyList<GitIdentity> _identities;

    public OwnerRouteEditForm(
        IReadOnlyList<GitServiceInstance> services,
        IReadOnlyList<GitIdentity> identities,
        RepositoryRoute? route = null)
    {
        _original = route;
        _identities = identities;
        Text = route is null ? "新建仓库路由" : "编辑仓库路由";
        UiHelpers.ConfigureDialog(this, 620, 260);

        var serviceChoices = services
            .Select(item => new ServiceChoice(item.Id, $"{item.DisplayName} ({item.HostName})"))
            .ToList();
        _service.DataSource = serviceChoices;
        _service.DisplayMember = nameof(ServiceChoice.DisplayText);
        _service.ValueMember = nameof(ServiceChoice.Id);
        _service.SelectedIndexChanged += (_, _) => RefreshIdentityChoices();
        _service.SelectedIndex = serviceChoices.Count == 1 ? 0 : -1;

        var table = UiHelpers.CreateCompactDialogTable(2, 130);
        UiHelpers.AddCompactDialogRow(table, 0, "Git 服务", _service);
        UiHelpers.AddCompactDialogRow(table, 1, "Owner / Namespace", _namespace);
        UiHelpers.AddCompactDialogRow(table, 2, "目标身份", _identity);
        UiHelpers.AddCompactDialogRow(table, 3, "状态", _enabled);

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

        if (route is not null)
        {
            _service.SelectedValue = route.ServiceInstanceId;
            RefreshIdentityChoices();
            _namespace.Text = route.NamespacePath;
            _identity.SelectedValue = route.IdentityId;
            _enabled.Checked = route.Enabled;
        }
    }

    public RepositoryRoute? ResultRoute { get; private set; }

    public string? OriginalServiceInstanceId => _original?.ServiceInstanceId;

    public string? OriginalNamespacePath => _original?.NamespacePath;

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (_service.SelectedValue is not string serviceInstanceId
            || string.IsNullOrWhiteSpace(_namespace.Text)
            || _identity.SelectedValue is not string identityId)
        {
            MessageBox.Show(this, "Git 服务、Owner / Namespace 和目标身份为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultRoute = new RepositoryRoute
        {
            ServiceInstanceId = serviceInstanceId,
            NamespacePath = _namespace.Text.Trim().Trim('/'),
            IdentityId = identityId,
            Enabled = _enabled.Checked
        };
    }

    private void RefreshIdentityChoices()
    {
        var selectedIdentityId = _identity.SelectedValue as string ?? _original?.IdentityId;
        var serviceInstanceId = _service.SelectedValue as string;
        var choices = _identities
            .Where(item => string.Equals(item.ServiceInstanceId, serviceInstanceId, StringComparison.OrdinalIgnoreCase))
            .Select(item => new IdentityChoice(item.Id, $"{item.DisplayName} ({item.AccountName} / {item.HostAlias})"))
            .ToList();
        _identity.DataSource = choices;
        _identity.DisplayMember = nameof(IdentityChoice.DisplayText);
        _identity.ValueMember = nameof(IdentityChoice.Id);
        if (!string.IsNullOrWhiteSpace(selectedIdentityId)
            && choices.Any(item => string.Equals(item.Id, selectedIdentityId, StringComparison.OrdinalIgnoreCase)))
        {
            _identity.SelectedValue = selectedIdentityId;
        }
        else
        {
            _identity.SelectedIndex = choices.Count == 1 ? 0 : -1;
        }
    }

    private sealed record ServiceChoice(string Id, string DisplayText);
    private sealed record IdentityChoice(string Id, string DisplayText);
}
