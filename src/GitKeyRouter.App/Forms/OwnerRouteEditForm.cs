using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class OwnerRouteEditForm : Form
{
    private readonly TextBox _owner = new() { PlaceholderText = "例如：openai 或你的 GitHub 组织名" };
    private readonly ComboBox _identity = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _enabled = new() { Text = "启用", Checked = true, AutoSize = true };
    private readonly RepositoryRoute? _original;

    public OwnerRouteEditForm(IReadOnlyList<GitIdentity> identities, RepositoryRoute? route = null)
    {
        _original = route;
        Text = route is null ? "新建 Owner 路由" : "编辑 Owner 路由";
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 260;
        MinimizeBox = false;
        MaximizeBox = false;

        var choices = identities.Select(item => new IdentityChoice(item.Id, $"{item.DisplayName} ({item.HostAlias})")).ToList();
        _identity.DataSource = choices;
        _identity.DisplayMember = nameof(IdentityChoice.DisplayText);
        _identity.ValueMember = nameof(IdentityChoice.Id);
        if (route is null)
        {
            _identity.SelectedIndex = -1;
        }

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "GitHub Owner", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_owner, 1, 0);
        table.Controls.Add(new Label { Text = "目标身份", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_identity, 1, 1);
        table.Controls.Add(new Label { Text = "状态", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_enabled, 1, 2);
        _owner.Dock = DockStyle.Fill;
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
            _owner.Text = route.GitHubOwner;
            _identity.SelectedValue = route.IdentityId;
            _enabled.Checked = route.Enabled;
        }
    }

    public RepositoryRoute? ResultRoute { get; private set; }

    public string? OriginalOwner => _original?.GitHubOwner;

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_owner.Text) || _identity.SelectedValue is not string identityId)
        {
            MessageBox.Show(this, "GitHub Owner 和目标身份为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultRoute = new RepositoryRoute
        {
            GitHubOwner = _owner.Text.Trim(),
            IdentityId = identityId,
            Enabled = _enabled.Checked
        };
    }

    private sealed record IdentityChoice(string Id, string DisplayText);
}
