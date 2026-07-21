using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class GitServicesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private IReadOnlyList<GitServiceInstance> _items = [];
    private IReadOnlyList<GitIdentity> _identities = [];

    public GitServicesControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = UiHelpers.CreatePageHeader("Git 服务", "管理 GitHub.com、GitLab、Gitea 与其他自建 Git 服务实例");
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("新建", async (_, _) => await CreateAsync()));
        toolbar.Controls.Add(UiHelpers.Button("编辑", async (_, _) => await EditAsync()));
        toolbar.Controls.Add(UiHelpers.Button("删除", async (_, _) => await DeleteAsync()));
        toolbar.Controls.Add(UiHelpers.Button("测试连接", async (_, _) => await TestConnectionAsync()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        _grid.CellDoubleClick += async (_, _) => await EditAsync();
    }

    public async Task RefreshAsync()
    {
        var config = await _services.ConfigStore.LoadAsync();
        _items = await _services.GitServiceService.ListAsync();
        _identities = config.Identities;
        _grid.DataSource = _items.Select(item => new ServiceRow
        {
            Id = item.Id,
            名称 = item.DisplayName,
            类型 = item.ProviderKind.ToString(),
            主机 = item.HostName,
            SSH端口 = item.SshPort?.ToString() ?? "默认",
            SSH用户 = item.SshUser,
            Web地址 = item.WebBaseUrl,
            默认身份 = _identities.FirstOrDefault(identity => string.Equals(identity.Id, item.DefaultIdentityId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? "<无>",
            服务级路由 = config.RepositoryRoutes.Any(route => route.Enabled
                && route.Scope == GitRouteScope.Service
                && string.Equals(route.ServiceInstanceId, item.Id, StringComparison.OrdinalIgnoreCase)) ? "已启用" : "未启用",
            内置 = item.IsBuiltIn ? "是" : "否"
        }).ToList();
        _status($"已加载 {_items.Count} 个 Git 服务");
    }

    private async Task CreateAsync()
    {
        using var form = new GitServiceEditForm(identities: _identities);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultService is null)
        {
            return;
        }

        await SaveAsync(form.ResultService);
    }

    private async Task EditAsync()
    {
        var selected = SelectedService();
        if (selected is null)
        {
            return;
        }

        using var form = new GitServiceEditForm(selected, _identities);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultService is null)
        {
            return;
        }

        await SaveAsync(form.ResultService);
    }

    private async Task SaveAsync(GitServiceInstance service)
    {
        var result = await _services.GitServiceService.SaveAsync(service);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task DeleteAsync()
    {
        var selected = SelectedService();
        if (selected is null)
        {
            return;
        }

        if (selected.IsBuiltIn)
        {
            MessageBox.Show(this, "内置 GitHub.com 服务不能删除。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"删除 Git 服务“{selected.DisplayName}”？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var result = await _services.GitServiceService.DeleteAsync(selected.Id);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task TestConnectionAsync()
    {
        var selected = SelectedService();
        if (selected is null)
        {
            return;
        }

        _status($"正在测试 {selected.DisplayName} 的 SSH 连接...");
        var result = await _services.GitServiceService.TestConnectionAsync(selected);
        if (!result.Success || result.Value is null)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        CommandResultForm.ShowProcess(this, $"{selected.DisplayName} - {result.Value.Classification}", result.Value.Process);
        _status(result.Value.Authenticated ? "Git 服务认证成功" : result.Value.Classification);
    }

    private GitServiceInstance? SelectedService()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ServiceRow row)
        {
            MessageBox.Show(this, "请先选择一个 Git 服务。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _items.FirstOrDefault(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ServiceRow
    {
        public string Id { get; init; } = string.Empty;
        public string 名称 { get; init; } = string.Empty;
        public string 类型 { get; init; } = string.Empty;
        public string 主机 { get; init; } = string.Empty;
        public string SSH端口 { get; init; } = string.Empty;
        public string SSH用户 { get; init; } = string.Empty;
        public string Web地址 { get; init; } = string.Empty;
        public string 默认身份 { get; init; } = string.Empty;
        public string 服务级路由 { get; init; } = string.Empty;
        public string 内置 { get; init; } = string.Empty;
    }
}
