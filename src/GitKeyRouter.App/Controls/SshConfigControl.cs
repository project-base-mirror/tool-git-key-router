using System.Text;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Controls;

public sealed class SshConfigControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _managedGrid = UiHelpers.CreateGrid();
    private readonly TextBox _parsedText = CreateTextBox();
    private readonly TextBox _rawText = CreateTextBox();
    private string _raw = string.Empty;
    private IReadOnlyList<SshManagedBlock> _blocks = [];

    public SshConfigControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;

        var header = new Label
        {
            Text = "SSH Config",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI Semibold", 18F)
        };
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("同步全部身份", async (_, _) => await SynchronizeAllAsync()));
        toolbar.Controls.Add(UiHelpers.Button("删除选中区块", async (_, _) => await DeleteSelectedAsync()));
        toolbar.Controls.Add(UiHelpers.Button("编辑原始文本", async (_, _) => await EditRawAsync()));
        toolbar.Controls.Add(UiHelpers.Button("恢复最近备份", async (_, _) => await RestoreLatestAsync()));
        toolbar.Controls.Add(UiHelpers.Button("打开文件", (_, _) => OpenConfig()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("受管理 Host") { Controls = { _managedGrid } });
        tabs.TabPages.Add(new TabPage("解析结果") { Controls = { _parsedText } });
        tabs.TabPages.Add(new TabPage("原始文本") { Controls = { _rawText } });

        Controls.Add(tabs);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    public async Task RefreshAsync()
    {
        _raw = await _services.SshConfigService.ReadRawAsync();
        _blocks = _services.SshConfigService.ParseManagedBlocks(_raw);
        _managedGrid.DataSource = _blocks.Select(block => new ManagedRow
        {
            HostAlias = block.HostAlias,
            起始位置 = block.StartIndex,
            长度 = block.Length,
            状态 = _blocks.Count(item => string.Equals(item.HostAlias, block.HostAlias, StringComparison.OrdinalIgnoreCase)) == 1 ? "正常" : "重复"
        }).ToList();
        _rawText.Text = string.IsNullOrEmpty(_raw) ? "<SSH config 文件不存在或为空>" : _raw;

        var parsed = new StringBuilder();
        parsed.AppendLine($"路径：{_services.Paths.SshConfigPath}");
        parsed.AppendLine($"受管理区块：{_blocks.Count}");
        var hostLines = _raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        parsed.AppendLine($"全部 Host 声明：{hostLines.Count}");
        foreach (var hostLine in hostLines)
        {
            parsed.AppendLine($"  {hostLine}");
        }
        parsed.AppendLine();
        foreach (var block in _blocks)
        {
            parsed.AppendLine($"HostAlias: {block.HostAlias}");
            parsed.AppendLine(block.RawText);
            parsed.AppendLine(new string('-', 60));
        }

        _parsedText.Text = parsed.ToString();
        _status($"SSH Config 已读取，受管理区块 {_blocks.Count} 个");
    }

    private async Task SynchronizeAllAsync()
    {
        var config = await _services.ConfigStore.LoadAsync();
        var preview = _services.SshConfigService.PreviewSynchronizeAll(_raw, config.Identities);
        using var diff = new DiffPreviewForm("同步全部 SSH managed block", preview.DiffText);
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.SshConfigService.ApplyAsync(preview, "Synchronize all SSH managed blocks");
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_managedGrid.CurrentRow?.DataBoundItem is not ManagedRow row)
        {
            MessageBox.Show(this, "请先选择一个受管理区块。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preview = _services.SshConfigService.PreviewDelete(_raw, row.HostAlias);
        using var diff = new DiffPreviewForm($"删除 SSH managed block: {row.HostAlias}", preview.DiffText);
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.SshConfigService.ApplyAsync(preview, $"Delete SSH managed block: {row.HostAlias}");
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task EditRawAsync()
    {
        var warning = MessageBox.Show(
            this,
            "原始文本编辑会替换完整 SSH config 文件。保存前仍会显示实际 diff 并创建备份。\r\n\r\n普通身份修改建议使用 managed block 操作。是否继续？",
            "编辑 SSH Config 原始文本",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (warning != DialogResult.Yes)
        {
            return;
        }

        using var editor = new TextViewForm("编辑 SSH Config 原始文本", _raw, editable: true);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var updated = editor.ContentTextBox.Text;
        var preview = new ChangePreview
        {
            Description = "Replace SSH config raw text",
            OriginalText = _raw,
            UpdatedText = updated,
            DiffText = TextDiffService.CreateSimpleDiff(_raw, updated, "ssh_config.before", "ssh_config.after")
        };
        using var diff = new DiffPreviewForm("SSH Config 完整文件 diff", preview.DiffText, "确认保存完整文件");
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.SshConfigService.ApplyAsync(preview, "Replace SSH config raw text");
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private async Task RestoreLatestAsync()
    {
        var backup = (await _services.BackupService.ListAsync()).FirstOrDefault();
        if (backup is null)
        {
            MessageBox.Show(this, "没有可用备份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var snapshot = await _services.BackupService.ReadAsync(backup.BackupDirectory);
        var target = snapshot.Manifest.SshConfigExisted ? snapshot.SshConfigText ?? string.Empty : string.Empty;
        var diffText = TextDiffService.CreateSimpleDiff(_raw, target, "ssh_config.current", "ssh_config.backup");
        using var diff = new DiffPreviewForm($"恢复 SSH Config - {backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}", diffText, "恢复备份");
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.BackupService.RestoreSshConfigAsync(backup.BackupDirectory);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
    }

    private void OpenConfig()
    {
        Directory.CreateDirectory(_services.Paths.SshDirectory);
        if (!_services.FileSystem.FileExists(_services.Paths.SshConfigPath))
        {
            MessageBox.Show(this, $"SSH Config 尚不存在：\r\n{_services.Paths.SshConfigPath}", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = _services.Paths.SshConfigPath,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(startInfo);
    }

    private static TextBox CreateTextBox()
        => new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F)
        };

    private sealed class ManagedRow
    {
        public string HostAlias { get; init; } = string.Empty;
        public int 起始位置 { get; init; }
        public int 长度 { get; init; }
        public string 状态 { get; init; } = string.Empty;
    }
}
