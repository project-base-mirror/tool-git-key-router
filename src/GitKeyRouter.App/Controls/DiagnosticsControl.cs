using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Diagnostics;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class DiagnosticsControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private readonly TextBox _reportText = UiHelpers.CreateOutputTextBox(
        wordWrap: false,
        scrollBars: ScrollBars.Both);
    private readonly Panel _reportPanel;
    private DiagnosticReport? _report;

    public DiagnosticsControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = UiHelpers.CreatePageHeader(
            AppLocalization.T("诊断", "Diagnostics"),
            AppLocalization.T("集中检查工具链、配置文件、SSH 与 Git rewrite 状态", "Check toolchain, configuration files, SSH, and Git rewrite status in one place"),
            AppLocalization.T(
                "在新增身份、修改路由或恢复配置后运行一键诊断。\r\n\r\n• 错误通常需要立即处理。\r\n• 警告表示配置可用但存在风险或未同步。\r\n• 建议列给出下一步操作。\r\n• 导出的报告包含路径和状态，但不会包含私钥内容。",
                "Run diagnostics after adding identities, changing routes, or restoring configuration.\r\n\r\n• Errors normally require immediate action.\r\n• Warnings indicate risk or an unsynchronized state.\r\n• The Suggested action column provides the next step.\r\n• Exported reports include paths and status, but never private-key contents."));
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("一键诊断", "Run diagnostics"), async (_, _) => await RefreshAsync()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("复制诊断报告", "Copy diagnostic report"), (_, _) => CopyReport()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("导出报告", "Export report"), (_, _) => ExportReport()));
        toolbar.Controls.Add(UiHelpers.Button(AppLocalization.T("查看完整报告", "View full report"), (_, _) => ViewReport()));

        _reportPanel = UiHelpers.CreateOutputPanel(_reportText);
        _reportPanel.Dock = DockStyle.Bottom;
        _reportPanel.Height = 210;
        _reportPanel.Margin = new Padding(0, 8, 0, 0);

        Controls.Add(_grid);
        Controls.Add(_reportPanel);
        Controls.Add(toolbar);
        Controls.Add(header);
        UiHelpers.EnableStatusColors(_grid, nameof(DiagnosticRow.级别));
    }

    public async Task RefreshAsync()
    {
        _status("正在执行一键诊断...");
        _report = await _services.DiagnosticService.RunAsync();
        _grid.DataSource = _report.Items.Select(item => new DiagnosticRow
        {
            级别 = item.Severity.ToString(),
            分类 = item.Category,
            项目 = item.Title,
            结果 = item.Message.Replace(Environment.NewLine, " | ", StringComparison.Ordinal),
            建议 = item.SuggestedAction ?? string.Empty
        }).ToList();
        _reportText.Text = DiagnosticReportFormatter.Format(_report);
        _status($"诊断完成：正常 {_report.NormalCount}，警告 {_report.WarningCount}，错误 {_report.ErrorCount}");
    }

    private void CopyReport()
    {
        if (!string.IsNullOrWhiteSpace(_reportText.Text))
        {
            Clipboard.SetText(_reportText.Text);
            _status("诊断报告已复制，不包含私钥内容");
        }
    }

    private void ExportReport()
    {
        if (_report is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            FileName = $"GitKeyRouter-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _reportText.Text);
            _status($"诊断报告已导出：{dialog.FileName}");
        }
    }

    private void ViewReport()
    {
        using var form = new TextViewForm("完整诊断报告", _reportText.Text);
        form.ShowDialog(this);
    }

    private sealed class DiagnosticRow
    {
        public string 级别 { get; init; } = string.Empty;
        public string 分类 { get; init; } = string.Empty;
        public string 项目 { get; init; } = string.Empty;
        public string 结果 { get; init; } = string.Empty;
        public string 建议 { get; init; } = string.Empty;
    }
}
