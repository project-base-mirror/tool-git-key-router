using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class GitProfilesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _profilesGrid = UiHelpers.CreateGrid();
    private readonly DataGridView _rulesGrid = UiHelpers.CreateGrid();
    private readonly SplitContainer _split;
    private bool _splitLayoutInitialized;
    private bool _updatingSplitLayout;
    private AppConfig _config = new();

    public GitProfilesControl(ApplicationServices services, Action<string> status)
    {
        _services = services; _status = status;
        var header = UiHelpers.CreatePageHeader("Git Profiles", "按目录或远程 URL 自动选择 commit 的 user.name、user.email 与签名密钥");
        var profileToolbar = UiHelpers.CreateToolbar();
        profileToolbar.Controls.Add(UiHelpers.Button("新建 Profile", async (_, _) => await CreateProfileAsync()));
        profileToolbar.Controls.Add(UiHelpers.Button("编辑 Profile", async (_, _) => await EditProfileAsync()));
        profileToolbar.Controls.Add(UiHelpers.Button("删除 Profile", async (_, _) => await DeleteProfileAsync()));
        profileToolbar.Controls.Add(UiHelpers.Button("预览并应用", async (_, _) => await PreviewAndApplyAsync()));
        profileToolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));
        var ruleToolbar = UiHelpers.CreateToolbar();
        ruleToolbar.Controls.Add(UiHelpers.Button("新建规则", async (_, _) => await CreateRuleAsync()));
        ruleToolbar.Controls.Add(UiHelpers.Button("编辑规则", async (_, _) => await EditRuleAsync()));
        ruleToolbar.Controls.Add(UiHelpers.Button("删除规则", async (_, _) => await DeleteRuleAsync()));
        var profilesPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiHelpers.Surface };
        profilesPanel.Controls.Add(_profilesGrid); profilesPanel.Controls.Add(profileToolbar);
        var rulesPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiHelpers.Surface };
        rulesPanel.Controls.Add(_rulesGrid); rulesPanel.Controls.Add(ruleToolbar);
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 8,
            Panel1MinSize = 0,
            Panel2MinSize = 0,
            BackColor = UiHelpers.AppBackground
        };
        _split.Panel1.Controls.Add(profilesPanel);
        _split.Panel2.Controls.Add(rulesPanel);
        _split.SizeChanged += (_, _) => UpdateSplitLayout();
        Controls.Add(_split);
        Controls.Add(header);
        HandleCreated += (_, _) => ScheduleSplitLayout();
        _profilesGrid.SelectionChanged += (_, _) => RefreshRulesGrid();
        _profilesGrid.CellDoubleClick += async (_, _) => await EditProfileAsync();
        _rulesGrid.CellDoubleClick += async (_, _) => await EditRuleAsync();
    }

    private void ScheduleSplitLayout()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(UpdateSplitLayout));
        }
        catch (ObjectDisposedException)
        {
            // Closing the main form while layout is queued is safe to ignore.
        }
        catch (InvalidOperationException)
        {
            // The control can be disposed between HandleCreated and BeginInvoke.
        }
    }

    private void UpdateSplitLayout()
    {
        if (_updatingSplitLayout || _split.IsDisposed)
        {
            return;
        }

        var availableHeight = _split.ClientSize.Height - _split.SplitterWidth;
        if (availableHeight <= 0)
        {
            return;
        }

        const int preferredTopHeight = 285;
        const int minimumTopHeight = 180;
        const int minimumBottomHeight = 160;

        _updatingSplitLayout = true;
        try
        {
            if (availableHeight < minimumTopHeight + minimumBottomHeight)
            {
                _split.SplitterDistance = Math.Max(0, availableHeight / 2);
                return;
            }

            var desiredDistance = _splitLayoutInitialized
                ? _split.SplitterDistance
                : preferredTopHeight;
            _split.SplitterDistance = Math.Clamp(
                desiredDistance,
                minimumTopHeight,
                availableHeight - minimumBottomHeight);
            _splitLayoutInitialized = true;
        }
        finally
        {
            _updatingSplitLayout = false;
        }
    }

    public async Task RefreshAsync()
    {
        _config = await _services.ConfigStore.LoadAsync();
        _profilesGrid.DataSource = _config.GitProfiles
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(profile => new ProfileRow
            {
                Id = profile.Id,
                名称 = profile.DisplayName,
                UserName = profile.UserName,
                UserEmail = profile.UserEmail,
                签名 = profile.EnableCommitSigning ? "启用" : "关闭",
                默认服务 = ServiceName(profile.DefaultServiceInstanceId),
                默认身份 = IdentityName(profile.DefaultIdentityId),
                规则数 = _config.GitProfileRules.Count(item =>
                    string.Equals(item.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();
        HideIdColumn(_profilesGrid);
        RefreshRulesGrid();
        _status($"已加载 {_config.GitProfiles.Count} 个 Git Profile、{_config.GitProfileRules.Count} 条条件规则");
    }

    private void RefreshRulesGrid()
    {
        var profile = SelectedProfile(false);
        _rulesGrid.DataSource = profile is null ? new List<RuleRow>() : _config.GitProfileRules.Where(item => string.Equals(item.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Kind).ThenBy(item => item.Pattern, StringComparer.OrdinalIgnoreCase).Select(item => new RuleRow { Id = item.Id, 类型 = item.Kind == GitProfileRuleKind.Directory ? "目录" : "远程 URL", 匹配条件 = item.Pattern, 启用 = item.Enabled }).ToList();
        HideIdColumn(_rulesGrid);
    }

    private async Task CreateProfileAsync() { using var form = new GitProfileEditForm(_config.GitServices, _config.Identities); if (form.ShowDialog(this) == DialogResult.OK && form.ResultProfile is not null) await SaveProfileAsync(form.ResultProfile); }
    private async Task EditProfileAsync() { var profile = SelectedProfile(); if (profile is null) return; using var form = new GitProfileEditForm(_config.GitServices, _config.Identities, profile); if (form.ShowDialog(this) == DialogResult.OK && form.ResultProfile is not null) await SaveProfileAsync(form.ResultProfile); }
    private async Task SaveProfileAsync(GitProfile profile) { var result = await _services.GitProfileService.SaveProfileAsync(profile); if (!result.Success) { UiHelpers.ShowErrors(this, result); return; } await RefreshAsync(); }
    private async Task DeleteProfileAsync() { var profile = SelectedProfile(); if (profile is null || MessageBox.Show(this, $"删除 Git Profile“{profile.DisplayName}”及其全部规则？\r\n已生成的 Git 配置需再次点击“预览并应用”才能同步删除。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; var result = await _services.GitProfileService.DeleteProfileAsync(profile.Id); if (!result.Success) { UiHelpers.ShowErrors(this, result); return; } await RefreshAsync(); }
    private async Task CreateRuleAsync() { var profile = SelectedProfile(); if (profile is null) return; var service = _config.FindService(profile.DefaultServiceInstanceId); var suggestion = service is null ? null : service.WebBaseUrl.TrimEnd('/') + "/**"; using var form = new GitProfileRuleEditForm(profile, suggestedRemotePattern: suggestion); if (form.ShowDialog(this) == DialogResult.OK && form.ResultRule is not null) await SaveRuleAsync(form.ResultRule); }
    private async Task EditRuleAsync() { var profile = SelectedProfile(); var rule = SelectedRule(); if (profile is null || rule is null) return; using var form = new GitProfileRuleEditForm(profile, rule); if (form.ShowDialog(this) == DialogResult.OK && form.ResultRule is not null) await SaveRuleAsync(form.ResultRule); }
    private async Task SaveRuleAsync(GitProfileRule rule) { var result = await _services.GitProfileService.SaveRuleAsync(rule); if (!result.Success) { UiHelpers.ShowErrors(this, result); return; } await RefreshAsync(); }
    private async Task DeleteRuleAsync() { var rule = SelectedRule(); if (rule is null || MessageBox.Show(this, $"删除规则“{rule.Pattern}”？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; var result = await _services.GitProfileService.DeleteRuleAsync(rule.Id); if (!result.Success) { UiHelpers.ShowErrors(this, result); return; } await RefreshAsync(); }
    private async Task PreviewAndApplyAsync() { var preview = await _services.GitProfileService.BuildPreviewAsync(); using var diff = new DiffPreviewForm("Git Profile 条件配置", preview.DiffText, "应用配置"); if (diff.ShowDialog(this) != DialogResult.OK) return; var result = await _services.GitProfileService.ApplyAsync(preview); if (!result.Success || result.Value is null) { UiHelpers.ShowErrors(this, result); return; } _status($"已应用 {result.Value.ProfileFileCount} 个 Git Profile；入口：{result.Value.MasterConfigPath}"); MessageBox.Show(this, "Git Profile 条件配置已应用。\r\nGit 将按目录或远程 URL 自动选择提交身份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private GitProfile? SelectedProfile(bool showMessage = true) { if (_profilesGrid.CurrentRow?.DataBoundItem is not ProfileRow row) { if (showMessage) MessageBox.Show(this, "请先选择一个 Git Profile。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information); return null; } return _config.GitProfiles.FirstOrDefault(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase)); }
    private GitProfileRule? SelectedRule() { if (_rulesGrid.CurrentRow?.DataBoundItem is not RuleRow row) { MessageBox.Show(this, "请先选择一条 Profile 规则。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information); return null; } return _config.GitProfileRules.FirstOrDefault(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase)); }
    private string ServiceName(string id) => string.IsNullOrWhiteSpace(id) ? "<未指定>" : _config.FindService(id)?.DisplayName ?? $"缺失：{id}";
    private string IdentityName(string id) => string.IsNullOrWhiteSpace(id) ? "<未指定>" : _config.Identities.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? $"缺失：{id}";
    private static void HideIdColumn(DataGridView grid) { if (grid.Columns["Id"] is { } column) column.Visible = false; }
    private sealed class ProfileRow { public string Id { get; init; } = string.Empty; public string 名称 { get; init; } = string.Empty; public string UserName { get; init; } = string.Empty; public string UserEmail { get; init; } = string.Empty; public string 签名 { get; init; } = string.Empty; public string 默认服务 { get; init; } = string.Empty; public string 默认身份 { get; init; } = string.Empty; public int 规则数 { get; init; } }
    private sealed class RuleRow { public string Id { get; init; } = string.Empty; public string 类型 { get; init; } = string.Empty; public string 匹配条件 { get; init; } = string.Empty; public bool 启用 { get; init; } }
}
