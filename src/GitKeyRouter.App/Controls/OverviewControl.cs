using System.Text;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Controls;

public sealed class OverviewControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly TextBox _summary = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Segoe UI", 10F)
    };

    public OverviewControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;

        var header = new Label
        {
            Text = "概览",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI Semibold", 18F)
        };
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));
        toolbar.Controls.Add(UiHelpers.Button("一键诊断", async (_, _) => await RunDiagnosticsAsync()));
        toolbar.Controls.Add(UiHelpers.Button("打开配置目录", (_, _) => OpenDirectory(_services.Paths.AppDataDirectory)));

        Controls.Add(_summary);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public async Task RefreshAsync()
    {
        _status("正在读取环境和配置...");
        var toolsTask = _services.ToolchainService.InspectAsync();
        var configTask = _services.ConfigStore.LoadAsync();
        var backupsTask = _services.BackupService.ListAsync();
        var tools = await toolsTask;
        var config = await configTask;
        var backups = await backupsTask;

        var builder = new StringBuilder();
        builder.AppendLine("工具环境");
        builder.AppendLine(new string('─', 60));
        AppendTool(builder, tools.Git);
        AppendTool(builder, tools.Ssh);
        AppendTool(builder, tools.SshKeygen);
        builder.AppendLine();
        builder.AppendLine("配置状态");
        builder.AppendLine(new string('─', 60));
        builder.AppendLine($"身份数量：{config.Identities.Count}");
        builder.AppendLine($"启用 Owner 路由：{config.OwnerRoutes.Count(item => item.Enabled)}");
        builder.AppendLine($"配置文件：{_services.Paths.ConfigPath}");
        builder.AppendLine($"SSH Config：{_services.Paths.SshConfigPath}");
        builder.AppendLine($"最近备份：{backups.FirstOrDefault()?.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "无"}");
        builder.AppendLine();

        try
        {
            var comparisons = await _services.GitUrlRewriteService.CompareAsync();
            builder.AppendLine("Git rewrite 摘要");
            builder.AppendLine(new string('─', 60));
            foreach (var group in comparisons.GroupBy(item => item.Status).OrderBy(item => item.Key))
            {
                builder.AppendLine($"{group.Key}: {group.Count()}");
            }
        }
        catch (Exception exception)
        {
            builder.AppendLine("Git rewrite 暂不可用：");
            builder.AppendLine(exception.Message);
        }

        _summary.Text = builder.ToString();
        _status("概览已刷新");
    }

    private async Task RunDiagnosticsAsync()
    {
        _status("正在执行一键诊断...");
        var report = await _services.DiagnosticService.RunAsync();
        using var form = new Forms.TextViewForm("诊断报告", Core.Diagnostics.DiagnosticReportFormatter.Format(report));
        form.ShowDialog(this);
        _status($"诊断完成：错误 {report.ErrorCount}，警告 {report.WarningCount}");
    }

    private static void AppendTool(StringBuilder builder, Core.Models.ExecutableInfo tool)
    {
        builder.AppendLine($"{tool.Name}: {(tool.Exists ? "已找到" : "缺失")}");
        builder.AppendLine($"  路径：{tool.SelectedPath ?? "<未找到>"}");
        builder.AppendLine($"  版本：{tool.Version ?? "<未知>"}");
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(path);
        System.Diagnostics.Process.Start(startInfo);
    }
}
