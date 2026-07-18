using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class GitProfileRuleEditForm : Form
{
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _pattern = new();
    private readonly CheckBox _enabled = new() { Text = "启用", Checked = true, AutoSize = true };
    private readonly GitProfile _profile;
    private readonly GitProfileRule? _original;

    public GitProfileRuleEditForm(GitProfile profile, GitProfileRule? rule = null, string? suggestedRemotePattern = null)
    {
        _profile = profile;
        _original = rule;
        Text = rule is null ? "新建 Profile 规则" : "编辑 Profile 规则";
        UiHelpers.ConfigureDialog(this, 680, 250);
        _kind.DataSource = Enum.GetValues<GitProfileRuleKind>();
        _kind.SelectedIndexChanged += (_, _) => UpdatePatternHint();
        var browse = UiHelpers.CreateDialogButton("浏览...");
        browse.Click += (_, _) => BrowseDirectory();
        var table = UiHelpers.CreateCompactDialogTable(3, 130, 92);
        UiHelpers.AddCompactDialogRow(table, 0, "目标 Profile", new Label { Text = profile.DisplayName, AutoSize = true, ForeColor = UiHelpers.TextPrimary }, 2);
        UiHelpers.AddCompactDialogRow(table, 1, "匹配类型", _kind, 2);
        UiHelpers.AddCompactDialogRow(table, 2, "匹配条件", _pattern, browse);
        UiHelpers.AddCompactDialogRow(table, 3, "状态", _enabled, 2);
        var save = UiHelpers.CreateDialogButton("保存", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        save.Click += SaveClicked;
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
        Controls.Add(UiHelpers.CreateDialogButtonBar(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;
        if (rule is not null) { _kind.SelectedItem = rule.Kind; _pattern.Text = rule.Pattern; _enabled.Checked = rule.Enabled; }
        else { _kind.SelectedItem = string.IsNullOrWhiteSpace(suggestedRemotePattern) ? GitProfileRuleKind.Directory : GitProfileRuleKind.RemoteUrl; _pattern.Text = suggestedRemotePattern ?? string.Empty; }
        UpdatePatternHint();
    }

    public GitProfileRule? ResultRule { get; private set; }
    private void UpdatePatternHint() => _pattern.PlaceholderText = _kind.SelectedItem is GitProfileRuleKind.Directory ? @"例如：C:\code\work\**" : "例如：https://gitlab.example/company/**";
    private void BrowseDirectory()
    {
        if (_kind.SelectedItem is not GitProfileRuleKind.Directory) return;
        using var dialog = new FolderBrowserDialog { Description = "选择此 Profile 应生效的代码目录", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _pattern.Text = dialog.SelectedPath;
    }
    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(_pattern.Text)) { MessageBox.Show(this, "匹配条件不能为空。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning); DialogResult = DialogResult.None; return; }
        ResultRule = new GitProfileRule { Id = _original?.Id ?? Guid.NewGuid().ToString("N"), ProfileId = _profile.Id, Kind = _kind.SelectedItem is GitProfileRuleKind kind ? kind : GitProfileRuleKind.Directory, Pattern = _pattern.Text.Trim(), Enabled = _enabled.Checked };
    }
}
