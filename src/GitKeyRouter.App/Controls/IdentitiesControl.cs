using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Controls;

public sealed class IdentitiesControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly DataGridView _grid = UiHelpers.CreateGrid();
    private IReadOnlyList<GitHubIdentity> _identities = [];

    public IdentitiesControl(ApplicationServices services, Action<string> status)
    {
        _services = services;
        _status = status;

        var header = new Label
        {
            Text = "GitHub 身份",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font("Segoe UI Semibold", 18F)
        };
        var toolbar = UiHelpers.CreateToolbar();
        toolbar.Controls.Add(UiHelpers.Button("新建", async (_, _) => await CreateAsync()));
        toolbar.Controls.Add(UiHelpers.Button("编辑", async (_, _) => await EditAsync()));
        toolbar.Controls.Add(UiHelpers.Button("删除记录", async (_, _) => await DeleteAsync()));
        toolbar.Controls.Add(UiHelpers.Button("生成密钥", async (_, _) => await GenerateKeyAsync()));
        toolbar.Controls.Add(UiHelpers.Button("查看公钥", async (_, _) => await ViewPublicKeyAsync(false)));
        toolbar.Controls.Add(UiHelpers.Button("复制公钥", async (_, _) => await ViewPublicKeyAsync(true)));
        toolbar.Controls.Add(UiHelpers.Button("导出公钥", async (_, _) => await ExportPublicKeyAsync()));
        toolbar.Controls.Add(UiHelpers.Button("同步 SSH Config", async (_, _) => await SyncSshAsync()));
        toolbar.Controls.Add(UiHelpers.Button("测试 SSH", async (_, _) => await TestSshAsync(false)));
        toolbar.Controls.Add(UiHelpers.Button("详细测试", async (_, _) => await TestSshAsync(true)));
        toolbar.Controls.Add(UiHelpers.Button("打开密钥目录", (_, _) => OpenKeyDirectory()));
        toolbar.Controls.Add(UiHelpers.Button("刷新", async (_, _) => await RefreshAsync()));

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);
        _grid.CellDoubleClick += async (_, _) => await EditAsync();
    }

    public async Task RefreshAsync()
    {
        _identities = await _services.IdentityService.ListAsync();
        var raw = await _services.SshConfigService.ReadRawAsync();
        var blocks = _services.SshConfigService.ParseManagedBlocks(raw);
        _grid.DataSource = _identities.Select(identity => new IdentityRow
        {
            Id = identity.Id,
            显示名称 = identity.DisplayName,
            GitHub用户名 = identity.GitHubUsername,
            HostAlias = identity.HostAlias,
            私钥 = _services.FileSystem.FileExists(identity.PrivateKeyPath) ? "存在" : "缺失",
            公钥 = _services.FileSystem.FileExists(identity.PublicKeyPath) ? "存在" : "缺失",
            SSH配置 = blocks.Count(block => string.Equals(block.HostAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase)) == 1 ? "正常" : "未同步"
        }).ToList();
        if (_grid.Columns[nameof(IdentityRow.Id)] is { } idColumn)
        {
            idColumn.Visible = false;
        }

        _status($"已加载 {_identities.Count} 个身份");
    }

    private async Task CreateAsync()
    {
        using var form = new IdentityEditForm(_services.Paths.SshDirectory);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultIdentity is null)
        {
            return;
        }

        var result = await _services.IdentityService.SaveAsync(form.ResultIdentity);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
        await OfferSshSyncAsync(form.ResultIdentity, null);
    }

    private async Task EditAsync()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        var oldAlias = identity.HostAlias;
        using var form = new IdentityEditForm(_services.Paths.SshDirectory, identity);
        if (form.ShowDialog(this) != DialogResult.OK || form.ResultIdentity is null)
        {
            return;
        }

        var result = await _services.IdentityService.SaveAsync(form.ResultIdentity);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
        await OfferSshSyncAsync(form.ResultIdentity, oldAlias);
    }

    private async Task DeleteAsync()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"删除身份记录“{identity.DisplayName}”？\r\n\r\n私钥和公钥文件不会被删除，关联 Owner 路由会被禁用。",
            "确认删除身份",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        var result = await _services.IdentityService.DeleteAsync(identity.Id);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        var raw = await _services.SshConfigService.ReadRawAsync();
        var preview = _services.SshConfigService.PreviewDelete(raw, identity.HostAlias);
        if (preview.HasChanges
            && MessageBox.Show(this, "同时删除该身份对应的 SSH managed block？", "SSH Config", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            using var diff = new DiffPreviewForm("删除 SSH managed block", preview.DiffText);
            if (diff.ShowDialog(this) == DialogResult.OK)
            {
                await _services.SshConfigService.ApplyAsync(preview, $"Delete SSH block after identity deletion: {identity.HostAlias}");
            }
        }

        await RefreshAsync();
    }

    private async Task GenerateKeyAsync()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        var exists = _services.FileSystem.FileExists(identity.PrivateKeyPath) || _services.FileSystem.FileExists(identity.PublicKeyPath);
        var overwrite = false;
        if (exists)
        {
            var answer = MessageBox.Show(
                this,
                "目标密钥文件已经存在。\r\n\r\n是：明确覆盖并先备份旧文件\r\n否：返回编辑身份，选择其他文件名\r\n取消：不执行",
                "密钥文件已存在",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel)
            {
                return;
            }

            if (answer == DialogResult.No)
            {
                await EditAsync();
                return;
            }

            overwrite = true;
        }

        var warning = MessageBox.Show(
            this,
            "将调用系统 ssh-keygen 生成 ed25519 密钥。\r\n\r\n第一版默认不设置 passphrase。是否继续？",
            "生成 SSH 密钥",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (warning != DialogResult.Yes)
        {
            return;
        }

        _status("正在调用 ssh-keygen...");
        var result = await _services.SshKeyService.GenerateAsync(identity, overwrite);
        if (!result.Success || result.Value is null)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        CommandResultForm.ShowProcess(this, "ssh-keygen 执行结果", result.Value.Process);
        using var publicKey = new TextViewForm("生成后的公钥", result.Value.PublicKeyText);
        publicKey.ShowDialog(this);
        await RefreshAsync();
        _status("SSH 密钥生成完成");
    }

    private async Task ViewPublicKeyAsync(bool copyOnly)
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        var result = await _services.SshKeyService.ReadPublicKeyAsync(identity);
        if (!result.Success || result.Value is null)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        if (copyOnly)
        {
            Clipboard.SetText(result.Value);
            _status("公钥已复制到剪贴板");
            return;
        }

        using var form = new TextViewForm($"公钥 - {identity.DisplayName}", result.Value);
        form.ShowDialog(this);
    }

    private async Task ExportPublicKeyAsync()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(identity.PublicKeyPath),
            Filter = "Public key (*.pub)|*.pub|All files (*.*)|*.*",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.SshKeyService.ExportPublicKeyAsync(identity, dialog.FileName, true);
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        _status($"公钥已导出：{dialog.FileName}");
    }

    private async Task SyncSshAsync()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        await OfferSshSyncAsync(identity, null, forcePrompt: true);
    }

    private async Task OfferSshSyncAsync(GitHubIdentity identity, string? previousAlias, bool forcePrompt = false)
    {
        if (!forcePrompt
            && MessageBox.Show(this, "身份已保存。是否立即同步对应的 SSH Config managed block？", "同步 SSH Config", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var raw = await _services.SshConfigService.ReadRawAsync();
        var updated = raw;
        if (!string.IsNullOrWhiteSpace(previousAlias)
            && !string.Equals(previousAlias, identity.HostAlias, StringComparison.OrdinalIgnoreCase))
        {
            updated = _services.SshConfigService.PreviewDelete(updated, previousAlias).UpdatedText;
        }

        updated = _services.SshConfigService.PreviewUpsert(updated, identity).UpdatedText;
        var preview = new ChangePreview
        {
            Description = $"Synchronize SSH identity: {identity.HostAlias}",
            OriginalText = raw,
            UpdatedText = updated,
            DiffText = TextDiffService.CreateSimpleDiff(raw, updated, "ssh_config.before", "ssh_config.after")
        };
        using var diff = new DiffPreviewForm("SSH Config 变更预览", preview.DiffText);
        if (diff.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = await _services.SshConfigService.ApplyAsync(preview, $"Synchronize identity SSH block: {identity.HostAlias}");
        if (!result.Success)
        {
            UiHelpers.ShowErrors(this, result);
            return;
        }

        await RefreshAsync();
        _status("SSH Config 已同步");
    }

    private async Task TestSshAsync(bool verbose)
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        _status($"正在测试 SSH：{identity.HostAlias}");
        var result = await _services.SshKeyService.TestAsync(identity.HostAlias, verbose);
        var text = $"Classification: {result.Classification}\r\nAuthenticated: {result.Authenticated}\r\n\r\n{UiHelpers.FormatProcess(result.Process)}";
        using var form = new CommandResultForm("SSH 测试结果", text);
        form.ShowDialog(this);
        _status(result.Authenticated ? "SSH 认证成功" : "SSH 测试未通过");
    }

    private void OpenKeyDirectory()
    {
        var identity = SelectedIdentity();
        if (identity is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(identity.PrivateKeyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var startInfo = new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
        startInfo.ArgumentList.Add(directory);
        System.Diagnostics.Process.Start(startInfo);
    }

    private GitHubIdentity? SelectedIdentity()
    {
        if (_grid.CurrentRow?.DataBoundItem is not IdentityRow row)
        {
            MessageBox.Show(this, "请先选择一个身份。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return _identities.FirstOrDefault(item => string.Equals(item.Id, row.Id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class IdentityRow
    {
        public string Id { get; init; } = string.Empty;
        public string 显示名称 { get; init; } = string.Empty;
        public string GitHub用户名 { get; init; } = string.Empty;
        public string HostAlias { get; init; } = string.Empty;
        public string 私钥 { get; init; } = string.Empty;
        public string 公钥 { get; init; } = string.Empty;
        public string SSH配置 { get; init; } = string.Empty;
    }
}
