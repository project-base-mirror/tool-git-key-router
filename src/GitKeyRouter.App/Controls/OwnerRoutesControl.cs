using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class OwnerRoutesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private IReadOnlyList<RepositoryRoute> _routes = [];
    private IReadOnlyList<GitIdentity> _identities = [];
    private IReadOnlyList<GitServiceInstance> _gitServices = [];

    public OwnerRoutesControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = UiHelpers.CreatePageHeader(
            AppLocalization.T("仓库路由", "Repository Routes"),
            AppLocalization.T("按整个服务、Owner 或单仓库映射 SSH 身份；仓库级优先于 Owner，Owner 优先于服务级", "Map SSH identities by service, owner, or repository; repository routes override owner routes, which override service routes"),
            AppLocalization.T(
                "路由决定某个 Git URL 应使用哪个 SSH 身份。\r\n\r\n优先级：仓库路由 > Owner/Namespace 路由 > 服务默认身份。\r\n\r\n保存路由后，使用“应用缺失规则”补充规则，或使用“修复全部路由”使全局 Git 配置与当前配置完全一致。执行前会显示预览。",
                "Routes choose the SSH identity for a Git URL.\r\n\r\nPriority: repository route > owner/namespace route > service default identity.\r\n\r\nAfter saving a route, use Apply missing rules to add only missing entries, or Reconcile all routes to make global Git configuration match the application. A preview is shown first."));
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("新建", "New"), async (_, _) => await CreateAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("编辑", "Edit"), async (_, _) => await EditAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("删除", "Delete"), async (_, _) => await DeleteAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("应用缺失规则", "Apply missing rules"), async (_, _) => await ApplyMissingAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("修复全部路由", "Reconcile all routes"), async (_, _) => await ReconcileAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("删除选中路由规则", "Remove selected route rules"), async (_, _) => await DeleteSelectedRulesAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("复制 Git 命令", "Copy Git commands"), (_, _) => CopyCommands()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("刷新", "Refresh"), async (_, _) => await RefreshAsync()));
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        _grid.CellDoubleClick += async (_, _) => await EditAsync();
        UiHelpers.EnableStatusColors(
            _grid,
            nameof(RouteRow.启用),
            nameof(RouteRow.HTTPS状态),
            nameof(RouteRow.SSH状态));
    }

    public async Task RefreshAsync()
    {
        var config = await _services.ConfigStore.LoadAsync();
        foreach (var route in config.RepositoryRoutes)
        {
            route.Normalize();
        }

        _routes = config.RepositoryRoutes
            .OrderBy(item => ServiceName(config.GitServices, item.ServiceInstanceId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Scope)
            .ThenBy(item => item.RoutePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _identities = config.Identities;
        _gitServices = config.GitServices;
        IReadOnlyList<GitRewriteComparison> comparisons;
        try
        {
            comparisons = await _services.GitUrlRewriteService.CompareAsync();
        }
        catch
        {
            comparisons = [];
        }

        _grid.DataSource = _routes.Select((route, index) =>
        {
            var identity = _identities.FirstOrDefault(item => string.Equals(item.Id, route.IdentityId, StringComparison.OrdinalIgnoreCase));
            var related = comparisons.Where(item =>
                string.Equals(item.ServiceInstanceId, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.NamespacePath, route.NamespacePath, StringComparison.OrdinalIgnoreCase)).ToList();
            return new RouteRow
            {
                Index = index,
                ServiceInstanceId = route.ServiceInstanceId,
                Git服务 = ServiceName(_gitServices, route.ServiceInstanceId),
                范围 = route.Scope.ToString(),
                路径 = route.DisplayPath,
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
        if (_grid.Columns[nameof(RouteRow.ServiceInstanceId)] is { } serviceIdColumn)
        {
            serviceIdColumn.Visible = false;
        }
        if (_grid.Columns[nameof(RouteRow.Index)] is { } indexColumn)
        {
            indexColumn.Visible = false;
        }

        _status($"已加载 {_routes.Count} 条仓库路由");
    }

    private async Task CreateAsync()
    {
        if (_identities.Count == 0)
        {
            MessageBox.Show(this, "请先创建至少一个 Git 身份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new OwnerRouteEditForm(_gitServices, _identities);
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

        using var form = new OwnerRouteEditForm(_gitServices, _identities, route);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultRoute is null)
        {
            return;
        }

        var result = await _services.OwnerRouteService.SaveAsync(
            form.ResultRoute,
            form.OriginalServiceInstanceId,
            form.OriginalNamespacePath);
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

        if (MessageBox.Show(this, $"删除仓库路由“{ServiceName(_gitServices, route.ServiceInstanceId)} / {route.DisplayPath}”？\r\nGit 全局配置不会在此步骤中自动删除。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var result = await _services.OwnerRouteService.DeleteByIdAsync(route.Id);
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
            await _services.GitUrlRewriteService.BuildDeleteRouteByIdPlanAsync(route.Id),
            $"删除 {route.DisplayPath} 的 Git rewrite",
            $"Delete Git URL rewrites for route: {route.ServiceInstanceId}/{route.DisplayPath}");
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
        var service = _gitServices.FirstOrDefault(item => string.Equals(item.Id, route.ServiceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (identity is null || service is null)
        {
            return;
        }

        var lines = _services.GitProviderAdapters.Get(service.ProviderKind)
            .BuildRewriteRules(service, identity, route)
            .Select(rule => $"git config --global --add \"url.{rule.BaseUrl}.insteadOf\" \"{rule.InsteadOfUrl}\"")
            .ToArray();
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
        _status("Git 命令已复制；程序实际执行时不会通过 shell 拼接这些文本");
    }

    private RepositoryRoute? SelectedRoute()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RouteRow row || row.Index < 0 || row.Index >= _routes.Count)
        {
            MessageBox.Show(this, "请先选择一条仓库路由。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _routes[row.Index];
    }

    private static string ServiceName(IEnumerable<GitServiceInstance> services, string serviceInstanceId)
        => services.FirstOrDefault(item => string.Equals(item.Id, serviceInstanceId, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? $"缺失：{serviceInstanceId}";

    private sealed class RouteRow
    {
        public int Index { get; init; }
        public string ServiceInstanceId { get; init; } = string.Empty;
        public string Git服务 { get; init; } = string.Empty;
        public string 范围 { get; init; } = string.Empty;
        public string 路径 { get; init; } = string.Empty;
        public string IdentityId { get; init; } = string.Empty;
        public string 身份 { get; init; } = string.Empty;
        public string HostAlias { get; init; } = string.Empty;
        public bool 启用 { get; init; }
        public string HTTPS状态 { get; init; } = string.Empty;
        public string SSH状态 { get; init; } = string.Empty;
    }
}
