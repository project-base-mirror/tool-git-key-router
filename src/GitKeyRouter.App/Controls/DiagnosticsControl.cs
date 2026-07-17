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
    private readonly TextBox _reportText = new()
    {
        Dock = DockStyle.Bottom,
        Height = 210,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9F)
    };
    private DiagnosticReport? _report;

    public DiagnosticsControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = new Label
        {
            Text = "诊断",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI Semibold", 18F)
        };
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("一键诊断", async (_, _) => await RefreshAsync()));
        toolbar.Controls.Add(UiHelpers.Button("复制诊断报告", (_, _) => CopyReport()));
        toolbar.Controls.Add(UiHelpers.Button("导出报告", (_, _) => ExportReport()));
        toolbar.Controls.Add(UiHelpers.Button("查看完整报告", (_, _) => ViewReport()));
        Controls.Add(_grid);
        Controls.Add(_reportText);
        Controls.Add(toolbar);
        Controls.Add(header);
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
