using System.Text;
using System.Text.Json;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Controls;

public sealed class BackupControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private IReadOnlyList<BackupManifest> _backups = [];

    public BackupControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;
        var header = new Label
        {
            Text = "备份与恢复",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI Semibold", 18F)
        };
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("立即创建快照", async (_, _) => await CreateSnapshotAsync()));
        toolbar.Controls.Add(UiHelpers.Button("查看内容", async (_, _) => await ViewAsync()));
        toolbar.Controls.Add(UiHelpers.Button("恢复 SSH Config", async (_, _) => await RestoreSshAsync()));
        toolbar.Controls.Add(UiHelpers.Button("恢复 Git rewrite", async (_, _) => await RestoreGitAsync()));
        toolbar.Controls.Add(UiHelpers.Button("恢复程序配置", async (_, _) => await RestoreAppAsync()));
        toolbar.Controls.Add(UiHelpers.Button("打开备份目录", (_, _) => OpenBackupDirectory()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));
        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public async Task RefreshAsync()
    {
        _backups = await _services.BackupService.ListAsync();
        _grid.DataSource = _backups.Select((item, index) => new BackupRow
        {
            Index = index,
            时间 = item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            原因 = item.Reason,
            SSH配置 = item.SshConfigExisted ? "有" : "原文件不存在",
            程序配置 = item.AppConfigExisted ? "有" : "原文件不存在",
            Git规则数 = item.GitRewriteCount,
            Git快照 = string.IsNullOrWhiteSpace(item.GitRewriteCaptureError) ? "正常" : "读取失败"
        }).ToList();
        if (_grid.Columns[nameof(BackupRow.Index)] is { } indexColumn)
        {
            indexColumn.Visible = false;
        }

        _status($"已加载 {_backups.Count} 个备份");
    }

    private async Task CreateSnapshotAsync()
    {
        var manifest = await _services.BackupService.CreateSnapshotAsync("Manual backup");
        _status($"已创建备份：{manifest.BackupDirectory}");
        await RefreshAsync();
    }

    private async Task ViewAsync()
    {
        var backup = SelectedBackup();
        if (backup is null)
        {
            return;
        }

        var snapshot = await _services.BackupService.ReadAsync(backup.BackupDirectory);
        var builder = new StringBuilder();
        builder.AppendLine("Manifest");
        builder.AppendLine(JsonSerializer.Serialize(snapshot.Manifest, new JsonSerializerOptions { WriteIndented = true }));
        builder.AppendLine();
        builder.AppendLine("Application config");
        builder.AppendLine(snapshot.AppConfigText ?? "<not present>");
        builder.AppendLine();
        builder.AppendLine("SSH config");
        builder.AppendLine(snapshot.SshConfigText ?? "<not present>");
        builder.AppendLine();
        builder.AppendLine("Git URL rewrites");
        foreach (var rule in snapshot.GitUrlRewrites)
        {
            builder.AppendLine($"{rule.ConfigKey} = {rule.InsteadOfUrl}");
        }

        using var form = new TextViewForm("备份内容", builder.ToString());
        form.ShowDialog(this);
    }

    private async Task RestoreSshAsync()
    {
        var backup = SelectedBackup();
        if (backup is null)
        {
            return;
        }

        var snapshot = await _services.BackupService.ReadAsync(backup.BackupDirectory);
        var current = await _services.SshConfigService.ReadRawAsync();
        var target = snapshot.Manifest.SshConfigExisted ? snapshot.SshConfigText ?? string.Empty : string.Empty;
        var diffText = TextDiffService.CreateSimpleDiff(current, target, "ssh_config.current", "ssh_config.backup");
        using var diff = new DiffPreviewForm("恢复 SSH Config", diffText, "恢复此备份");
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.BackupService.RestoreSshConfigAsync(backup.BackupDirectory);
        ShowRestoreResult(result);
    }

    private async Task RestoreAppAsync()
    {
        var backup = SelectedBackup();
        if (backup is null)
        {
            return;
        }

        var snapshot = await _services.BackupService.ReadAsync(backup.BackupDirectory);
        var current = _services.FileSystem.FileExists(_services.Paths.ConfigPath)
            ? await _services.FileSystem.ReadAllTextAsync(_services.Paths.ConfigPath)
            : string.Empty;
        var target = snapshot.Manifest.AppConfigExisted ? snapshot.AppConfigText ?? string.Empty : string.Empty;
        var diffText = TextDiffService.CreateSimpleDiff(current, target, "app_config.current", "app_config.backup");
        using var diff = new DiffPreviewForm("恢复程序配置", diffText, "恢复此备份");
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.BackupService.RestoreAppConfigAsync(backup.BackupDirectory);
        ShowRestoreResult(result);
    }

    private async Task RestoreGitAsync()
    {
        var backup = SelectedBackup();
        if (backup is null)
        {
            return;
        }

        var snapshot = await _services.BackupService.ReadAsync(backup.BackupDirectory);
        if (!string.IsNullOrWhiteSpace(snapshot.Manifest.GitRewriteCaptureError))
        {
            MessageBox.Show(this, snapshot.Manifest.GitRewriteCaptureError, "该备份没有可靠 Git 快照", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var current = await _services.GitUrlRewriteService.GetActualRulesAsync();
        var builder = new StringBuilder();
        builder.AppendLine("Current Git URL rewrites:");
        foreach (var rule in current)
        {
            builder.Append("- ").Append(rule.ConfigKey).Append(" = ").AppendLine(rule.InsteadOfUrl);
        }

        builder.AppendLine();
        builder.AppendLine("Backup Git URL rewrites:");
        foreach (var rule in snapshot.GitUrlRewrites)
        {
            builder.Append("+ ").Append(rule.ConfigKey).Append(" = ").AppendLine(rule.InsteadOfUrl);
        }

        using var diff = new DiffPreviewForm("恢复 Git URL rewrite", builder.ToString(), "恢复此备份");
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.BackupService.RestoreGitRewritesAsync(backup.BackupDirectory);
        ShowRestoreResult(result);
    }

    private void ShowRestoreResult(OperationResult result)
    {
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        MessageBox.Show(this, result.Message, "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _status(result.Message);
    }

    private BackupManifest? SelectedBackup()
    {
        if (_grid.CurrentRow?.DataBoundItem is not BackupRow row || row.Index < 0 || row.Index >= _backups.Count)
        {
            MessageBox.Show(this, "请先选择一个备份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _backups[row.Index];
    }

    private void OpenBackupDirectory()
    {
        Directory.CreateDirectory(_services.Paths.BackupRootDirectory);
        var startInfo = new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
        startInfo.ArgumentList.Add(_services.Paths.BackupRootDirectory);
        System.Diagnostics.Process.Start(startInfo);
    }

    private sealed class BackupRow
    {
        public int Index { get; init; }
        public string 时间 { get; init; } = string.Empty;
        public string 原因 { get; init; } = string.Empty;
        public string SSH配置 { get; init; } = string.Empty;
        public string 程序配置 { get; init; } = string.Empty;
        public int Git规则数 { get; init; }
        public string Git快照 { get; init; } = string.Empty;
    }
}
