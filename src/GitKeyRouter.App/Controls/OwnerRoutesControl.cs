using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class OwnerRoutesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private IReadOnlyList<OwnerRoute> _routes = [];
    private IReadOnlyList<GitHubIdentity> _identities = [];

    public OwnerRoutesControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = UiHelpers.CreatePageHeader("Owner 路由", "把不同 GitHub Owner 稳定映射到对应 SSH 身份");
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("新建", async (_, _) => await CreateAsync()));
        toolbar.Controls.Add(UiHelpers.Button("编辑", async (_, _) => await EditAsync()));
        toolbar.Controls.Add(UiHelpers.Button("删除", async (_, _) => await DeleteAsync()));
        toolbar.Controls.Add(UiHelpers.Button("应用缺失规则", async (_, _) => await ApplyMissingAsync()));
        toolbar.Controls.Add(UiHelpers.Button("修复全部路由", async (_, _) => await ReconcileAsync()));
        toolbar.Controls.Add(UiHelpers.Button("删除选中路由规则", async (_, _) => await DeleteSelectedRulesAsync()));
        toolbar.Controls.Add(UiHelpers.Button("复制 Git 命令", (_, _) => CopyCommands()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        _grid.CellDoubleClick += async (_, _) => await EditAsync();
    }

    public async Task RefreshAsync()
    {
        var config = await _services.ConfigStore.LoadAsync();
        _routes = config.OwnerRoutes.OrderBy(item => item.GitHubOwner, StringComparer.OrdinalIgnoreCase).ToList();
        _identities = config.Identities;
        IReadOnlyList<GitRewriteComparison> comparisons;
        try
        {
            comparisons = await _services.GitUrlRewriteService.CompareAsync();
        }
        catch
        {
            comparisons = [];
        }

        _grid.DataSource = _routes.Select(route =>
        {
            var identity = _identities.FirstOrDefault(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            var related = comparisons.Where(item => string.Equals(item.GitHubOwner, route.GitHubOwner, StringComparison.OrdinalIgnoreCase)).ToList();
            return new RouteRow
            {
                Owner = route.GitHubOwner,
                IdentityId = route.IdentityId,
                身份 = identity?.DisplayName ?? "<缺失>",
                HostAlias = identity?.HostAlias ?? "<缺失>",
                启用 = route.Enabled,
                HTTPS状态 = related.FirstOrDefault(item => item.InsteadOfUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))?.Status.ToString() ?? "未知",
                SSH状态 = related.FirstOrDefault(item => item.InsteadOfUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))?.Status.ToString() ?? "未知"
            };
        }).ToList();
        if (_grid.Columns[nameof(RouteRow.IdentityId)] is { } identityColumn)
        {
            identityColumn.Visible = false;
        }

        _status($"已加载 {_routes.Count} 条 Owner 路由");
    }

    private async Task CreateAsync()
    {
        if (_identities.Count == 0)
        {
            MessageBox.Show(this, "请先创建至少一个 GitHub 身份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new OwnerRouteEditForm(_identities);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultRoute is null)
        {
            return;
        }

        var result = await _services.OwnerRouteService.SaveAsync(form.ResultRoute);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task EditAsync()
    {
        var route = SelectedRoute();
        if (route is null)
        {
            return;
        }

        using var form = new OwnerRouteEditForm(_identities, route);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultRoute is null)
        {
            return;
        }

        var result = await _services.OwnerRouteService.SaveAsync(form.ResultRoute, form.OriginalOwner);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var route = SelectedRoute();
        if (route is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"删除 Owner 路由“{route.GitHubOwner}”？\r\nGit 全局配置不会在此步骤中自动删除。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var result = await _services.OwnerRouteService.DeleteAsync(route.GitHubOwner);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task ApplyMissingAsync()
        => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildApplyMissingPlanAsync(), "应用缺失 Git rewrite", "Apply missing Git URL rewrites");

    private async Task ReconcileAsync()
        => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildReconcilePlanAsync(), "修复 Git rewrite", "Reconcile Git URL rewrites");

    private async Task DeleteSelectedRulesAsync()
    {
        var route = SelectedRoute();
        if (route is null)
        {
            return;
        }

        await ApplyPlanAsync(
            await _services.GitUrlRewriteService.BuildDeleteOwnerPlanAsync(route.GitHubOwner),
            $"删除 {route.GitHubOwner} 的 Git rewrite",
            $"Delete Git URL rewrites for owner: {route.GitHubOwner}");
    }

    private async Task ApplyPlanAsync(GitRewritePlan plan, string title, string reason)
    {
        using var diff = new DiffPreviewForm(title, UiHelpers.FormatGitPlan(plan));
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.GitUrlRewriteService.ApplyPlanAsync(plan, reason);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        if (result.Value is { Count: > 0 })
        {
            var content = string.Join(Environment.NewLine + Environment.NewLine, result.Value.Select(UiHelpers.FormatProcess));
            using var command = new CommandResultForm("Git config 执行结果", content);
            command.ShowDialog(this);
        }

        await RefreshAsync();
    }

    private void CopyCommands()
    {
        var route = SelectedRoute();
        if (route is null)
        {
            return;
        }

        var identity = _identities.FirstOrDefault(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
        if (identity is null)
        {
            return;
        }

        var baseUrl = $"git@{identity.HostAlias}:{route.GitHubOwner}/";
        var lines = new[]
        {
            $"git config --global --add \"url.{baseUrl}.insteadOf\" \"https://github.com/{route.GitHubOwner}/\"",
            $"git config --global --add \"url.{baseUrl}.insteadOf\" \"git@github.com:{route.GitHubOwner}/\""
        };
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        _status("Git 命令已复制；程序实际执行时不会通过 shell 拼接这些文本");
    }

    private OwnerRoute? SelectedRoute()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RouteRow row)
        {
            MessageBox.Show(this, "请先选择一条 Owner 路由。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _routes.FirstOrDefault(item => string.Equals(item.GitHubOwner, row.Owner, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RouteRow
    {
        public string Owner { get; init; } = string.Empty;
        public string IdentityId { get; init; } = string.Empty;
        public string 身份 { get; init; } = string.Empty;
        public string HostAlias { get; init; } = string.Empty;
        public bool 启用 { get; init; }
        public string HTTPS状态 { get; init; } = string.Empty;
        public string SSH状态 { get; init; } = string.Empty;
    }
}
