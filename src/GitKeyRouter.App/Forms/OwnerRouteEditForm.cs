using GitKeyRouter.Core.Models;

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
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 310;
        MinimizeBox = false;
        MaximizeBox = false;

        var serviceChoices = services
            .Select(item => new ServiceChoice(item.Id, $"{item.DisplayName} ({item.HostName})"))
            .ToList();
        _service.DataSource = serviceChoices;
        _service.DisplayMember = nameof(ServiceChoice.DisplayText);
        _service.ValueMember = nameof(ServiceChoice.Id);
        _service.SelectedIndexChanged += (_, _) => RefreshIdentityChoices();
        _service.SelectedIndex = serviceChoices.Count == 1 ? 0 : -1;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(14) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Git 服务", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_service, 1, 0);
        table.Controls.Add(new Label { Text = "Owner / Namespace", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_namespace, 1, 1);
        table.Controls.Add(new Label { Text = "目标身份", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_identity, 1, 2);
        table.Controls.Add(new Label { Text = "状态", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        table.Controls.Add(_enabled, 1, 3);
        _service.Dock = DockStyle.Fill;
        _namespace.Dock = DockStyle.Fill;
        _identity.Dock = DockStyle.Fill;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var save = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += SaveClicked;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(table);
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
