using GitKeyRouter.Core.Models;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class OwnerRouteEditForm : Form
{
    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _owner = new() { PlaceholderText = "例如：project-base、camus0109" };
    private readonly TextBox _repository = new() { PlaceholderText = "例如：proto-tool-pb-extra.git" };
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
        UiHelpers.ConfigureDialog(this, 620, 340);

        var serviceChoices = services
            .Select(item => new ServiceChoice(item.Id, $"{item.DisplayName} ({item.HostName})"))
            .ToList();
        _service.DisplayMember = nameof(ServiceChoice.DisplayText);
        _service.Items.AddRange(serviceChoices.Cast<object>().ToArray());
        _service.SelectedIndexChanged += (_, _) => RefreshIdentityChoices();
        _service.SelectedIndex = serviceChoices.Count == 1 ? 0 : -1;
        _scope.Items.AddRange(Enum.GetValues<GitRouteScope>().Cast<object>().ToArray());
        _scope.SelectedItem = GitRouteScope.Owner;
        _scope.SelectedIndexChanged += (_, _) => RefreshScopeFields();

        var table = UiHelpers.CreateCompactDialogTable(2, 130);
        UiHelpers.AddCompactDialogRow(table, 0, "Git 服务", _service);
        UiHelpers.AddCompactDialogRow(table, 1, "路由范围", _scope);
        UiHelpers.AddCompactDialogRow(table, 2, "Owner", _owner);
        UiHelpers.AddCompactDialogRow(table, 3, "Repository", _repository);
        UiHelpers.AddCompactDialogRow(table, 4, "目标身份", _identity);
        UiHelpers.AddCompactDialogRow(table, 5, "状态", _enabled);

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
            _service.SelectedIndex = serviceChoices.FindIndex(item =>
                string.Equals(item.Id, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase));
            RefreshIdentityChoices();
            route.Normalize();
            _scope.SelectedItem = route.Scope;
            _owner.Text = route.Owner;
            _repository.Text = route.Repository;
            _identity.SelectedIndex = _identity.Items.Cast<IdentityChoice>().ToList().FindIndex(item =>
                string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            _enabled.Checked = route.Enabled;
        }

        RefreshScopeFields();
    }

    public RepositoryRoute? ResultRoute { get; private set; }

    public string? OriginalServiceInstanceId => _original?.ServiceInstanceId;

    public string? OriginalNamespacePath => _original?.NamespacePath;

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        var scope = _scope.SelectedItem is GitRouteScope selectedScope ? selectedScope : GitRouteScope.Owner;
        if (_service.SelectedItem is not ServiceChoice selectedService
            || _identity.SelectedItem is not IdentityChoice selectedIdentity
            || scope != GitRouteScope.Service && string.IsNullOrWhiteSpace(_owner.Text)
            || scope == GitRouteScope.Repository && string.IsNullOrWhiteSpace(_repository.Text))
        {
            MessageBox.Show(this, "Git 服务和目标身份为必填项；Owner/Repository 按所选范围填写。Owner 不会从登录账号自动推导。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultRoute = new RepositoryRoute
        {
            Id = _original?.Id ?? Guid.NewGuid().ToString("N"),
            ServiceInstanceId = selectedService.Id,
            Scope = scope,
            Owner = scope == GitRouteScope.Service ? null : _owner.Text.Trim().Trim('/'),
            Repository = scope == GitRouteScope.Repository ? _repository.Text.Trim().Trim('/') : null,
            IdentityId = selectedIdentity.Id,
            Enabled = _enabled.Checked
        };
        ResultRoute.Normalize();
    }

    private void RefreshIdentityChoices()
    {
        var selectedIdentityId = (_identity.SelectedItem as IdentityChoice)?.Id ?? _original?.IdentityId;
        var serviceInstanceId = (_service.SelectedItem as ServiceChoice)?.Id;
        var choices = _identities
            .Where(item => string.Equals(item.ServiceInstanceId, serviceInstanceId, StringComparison.OrdinalIgnoreCase))
            .Select(item => new IdentityChoice(item.Id, $"{item.DisplayName} ({item.AccountName} / {item.HostAlias})"))
            .ToList();
        _identity.BeginUpdate();
        _identity.Items.Clear();
        _identity.DisplayMember = nameof(IdentityChoice.DisplayText);
        _identity.Items.AddRange(choices.Cast<object>().ToArray());
        _identity.EndUpdate();
        if (!string.IsNullOrWhiteSpace(selectedIdentityId)
            && choices.Any(item => string.Equals(item.Id, selectedIdentityId, StringComparison.OrdinalIgnoreCase)))
        {
            _identity.SelectedIndex = choices.FindIndex(item =>
                string.Equals(item.Id, selectedIdentityId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _identity.SelectedIndex = choices.Count == 1 ? 0 : -1;
        }
    }

    private void RefreshScopeFields()
    {
        var scope = _scope.SelectedItem is GitRouteScope selected ? selected : GitRouteScope.Owner;
        _owner.Enabled = scope is GitRouteScope.Owner or GitRouteScope.Repository;
        _owner.Visible = _owner.Enabled;
        _repository.Enabled = scope == GitRouteScope.Repository;
        _repository.Visible = _repository.Enabled;
    }

    private sealed record ServiceChoice(string Id, string DisplayText);
    private sealed record IdentityChoice(string Id, string DisplayText);
}
