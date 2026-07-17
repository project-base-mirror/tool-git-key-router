using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class GitRewritesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private readonly TextBox _urlInput = new() { Dock = DockStyle.Fill, PlaceholderText = "https://github.com/owner/repository.git" };
    private readonly TextBox _previewOutput = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Height = 90, ScrollBars = ScrollBars.Vertical };
    private IReadOnlyList<GitRewriteComparison> _comparisons = [];

    public GitRewritesControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = UiHelpers.CreatePageHeader("Git 重写配置", "检查并修复 Git URL 到 SSH HostAlias 的重写规则");
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("应用缺失配置", async (_, _) => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildApplyMissingPlanAsync(), "应用缺失配置")));
        toolbar.Controls.Add(UiHelpers.Button("修复当前全部路由", async (_, _) => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildReconcilePlanAsync(), "修复 Git rewrite")));
        toolbar.Controls.Add(UiHelpers.Button("清理重复规则", async (_, _) => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildCleanupDuplicatesPlanAsync(), "清理重复 Git rewrite")));
        toolbar.Controls.Add(UiHelpers.Button("全部重新生成", async (_, _) => await ApplyPlanAsync(await _services.GitUrlRewriteService.BuildRegeneratePlanAsync(), "全部重新生成受管理 Git rewrite")));
        toolbar.Controls.Add(UiHelpers.Button("删除选中规则", async (_, _) => await DeleteSelectedAsync()));
        toolbar.Controls.Add(UiHelpers.Button("复制对应命令", (_, _) => CopySelectedCommand()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));

        var testPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 150,
            ColumnCount = 3,
            Padding = new Padding(6)
        };
        testPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        testPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        testPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        testPanel.Controls.Add(_urlInput, 0, 0);
        var previewButton = new Button { Text = "本地预览", Dock = DockStyle.Fill };
        previewButton.Click += async (_, _) => await PreviewUrlAsync();
        var connectButton = new Button { Text = "实际连接测试", Dock = DockStyle.Fill };
        connectButton.Click += async (_, _) => await TestConnectionAsync();
        testPanel.Controls.Add(previewButton, 1, 0);
        testPanel.Controls.Add(connectButton, 2, 0);
        testPanel.Controls.Add(_previewOutput, 0, 1);
        testPanel.SetColumnSpan(_previewOutput, 3);

        Controls.Add(_grid);
        Controls.Add(testPanel);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public async Task RefreshAsync()
    {
        try
        {
            _comparisons = await _services.GitUrlRewriteService.CompareAsync();
            _grid.DataSource = _comparisons.Select((item, index) => new RewriteRow
            {
                Index = index,
                Owner = item.GitHubOwner ?? "<额外配置>",
                Identity = item.IdentityDisplayName ?? string.Empty,
                BaseURL = item.ExpectedBaseUrl,
                InsteadOfURL = item.InsteadOfUrl,
                状态 = item.Status.ToString(),
                匹配数量 = item.ActualMatchCount
            }).ToList();
            if (_grid.Columns[nameof(RewriteRow.Index)] is { } indexColumn)
            {
                indexColumn.Visible = false;
            }

            _status($"已读取 {_comparisons.Count} 条 Git rewrite 对比结果");
        }
        catch (Exception exception)
        {
            _comparisons = [];
            _grid.DataSource = null;
            _previewOutput.Text = exception.ToString();
            _status("读取 Git rewrite 失败");
        }
    }

    private async Task ApplyPlanAsync(GitRewritePlan plan, string title)
    {
        using var diff = new DiffPreviewForm(title, UiHelpers.FormatGitPlan(plan));
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.GitUrlRewriteService.ApplyPlanAsync(plan, title);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        if (result.Value is { Count: > 0 })
        {
            using var command = new CommandResultForm(
                "Git config 执行结果",
                string.Join(Environment.NewLine + Environment.NewLine, result.Value.Select(UiHelpers.FormatProcess)));
            command.ShowDialog(this);
        }

        await RefreshAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedComparison();
        if (selected is null)
        {
            return;
        }

        var plan = await _services.GitUrlRewriteService.BuildDeleteRulePlanAsync(selected.ExpectedBaseUrl, selected.InsteadOfUrl);
        await ApplyPlanAsync(plan, "删除指定 Git rewrite");
    }

    private void CopySelectedCommand()
    {
        var selected = SelectedComparison();
        if (selected is null)
        {
            return;
        }

        var command = $"git config --global --add \"url.{selected.ExpectedBaseUrl}.insteadOf\" \"{selected.InsteadOfUrl}\"";
        Clipboard.SetText(command);
        _status("Git 命令已复制；实际执行使用独立参数，不使用此 shell 文本");
    }

    private async Task PreviewUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(_urlInput.Text))
        {
            return;
        }

        var preview = await _services.GitUrlRewriteService.PreviewAsync(_urlInput.Text.Trim());
        _previewOutput.Text = $"原始 URL：{preview.OriginalUrl}\r\n匹配前缀：{preview.MatchedPrefix ?? "<无>"}\r\n目标 Base：{preview.MatchedBaseUrl ?? "<无>"}\r\n重写结果：{preview.RewrittenUrl}";
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(_urlInput.Text))
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "实际连接测试将执行 git ls-remote，并可能等待网络和 SSH 认证。是否继续？",
                "实际连接测试",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _status("正在执行 git ls-remote...");
        var result = await _services.GitUrlRewriteService.TestRemoteAsync(_urlInput.Text.Trim());
        CommandResultForm.ShowProcess(this, "git ls-remote 结果", result);
        _status(result.Succeeded ? "Git 实际连接测试成功" : "Git 实际连接测试失败");
    }

    private GitRewriteComparison? SelectedComparison()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RewriteRow row || row.Index < 0 || row.Index >= _comparisons.Count)
        {
            MessageBox.Show(this, "请先选择一条 Git rewrite。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _comparisons[row.Index];
    }

    private sealed class RewriteRow
    {
        public int Index { get; init; }
        public string Owner { get; init; } = string.Empty;
        public string Identity { get; init; } = string.Empty;
        public string BaseURL { get; init; } = string.Empty;
        public string InsteadOfURL { get; init; } = string.Empty;
        public string 状态 { get; init; } = string.Empty;
        public int 匹配数量 { get; init; }
    }
}
