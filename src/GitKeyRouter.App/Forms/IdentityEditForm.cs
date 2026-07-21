using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using GitKeyRouter.App.Presentation;
using System.Runtime.InteropServices;

namespace GitKeyRouter.App.Forms;

public sealed class IdentityEditForm : Form
{
    private readonly string _sshDirectory;
    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _displayName = new() { PlaceholderText = "例如：个人 GitHub 或公司 GitLab" };
    private readonly TextBox _username = new() { PlaceholderText = "例如：octocat 或 camus" };
    private readonly TextBox _hostAlias = new() { PlaceholderText = "例如：github-personal 或 gitlab-work" };
    private readonly CueComboBox _privateKeyPath = new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        CueText = "选择自动发现的私钥，或输入完整路径"
    };
    private readonly TextBox _publicKeyPath = new() { PlaceholderText = "通常为私钥路径加 .pub" };
    private readonly TextBox _comment = new() { PlaceholderText = "例如：name@example.com（可选）" };
    private readonly Label _keyDiscoveryNote = new();
    private readonly GitIdentity _original;
    private string _lastSuggestedPublicKeyPath = string.Empty;

    public IdentityEditForm(
        string sshDirectory,
        IReadOnlyList<GitServiceInstance> services,
        GitIdentity? identity = null,
        IReadOnlyList<SshPrivateKeyCandidate>? discoveredKeys = null)
    {
        _sshDirectory = sshDirectory;
        _original = identity ?? new GitIdentity();
        Text = identity is null ? "新建 Git 身份" : "编辑 Git 身份";
        UiHelpers.ConfigureDialog(this, 760, 430);

        var table = UiHelpers.CreateCompactDialogTable(3, 130, 90);

        var serviceChoices = services
            .Select(item => new ServiceChoice(item.Id, $"{item.DisplayName} ({item.HostName})"))
            .ToList();
        _service.DisplayMember = nameof(ServiceChoice.DisplayText);
        _service.Items.AddRange(serviceChoices.Cast<object>().ToArray());
        _service.SelectedIndex = serviceChoices.Count == 1 ? 0 : -1;

        AddRow(table, 0, "Git 服务", _service);
        AddRow(table, 1, "显示名称", _displayName);
        AddRow(table, 2, "账号", _username);
        AddRow(table, 3, "HostAlias", _hostAlias);
        AddPathRow(table, 4, "私钥路径", _privateKeyPath, false);
        AddPathRow(table, 5, "公钥路径", _publicKeyPath, true);
        AddRow(table, 6, "注释 / Email", _comment);

        _keyDiscoveryNote.AutoSize = true;
        _keyDiscoveryNote.ForeColor = SystemColors.GrayText;
        _keyDiscoveryNote.MaximumSize = new Size(560, 0);
        UiHelpers.AddCompactDialogContent(table, 7, _keyDiscoveryNote, 1, 2);

        var save = UiHelpers.CreateDialogButton("保存", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        save.Click += SaveClicked;
        var buttons = UiHelpers.CreateDialogButtonBar(save, cancel);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
        LoadDiscoveredKeys(discoveredKeys ?? []);
        LoadValues(_original);
        _hostAlias.Leave += (_, _) => FillDefaultKeyPaths();
        _privateKeyPath.SelectionChangeCommitted += (_, _) => FillPublicKeyPathFromPrivate();
        _privateKeyPath.Leave += (_, _) => FillPublicKeyPathFromPrivate();
    }

    public GitIdentity? ResultIdentity { get; private set; }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        => UiHelpers.AddCompactDialogRow(table, row, label, control, 2);

    private void AddPathRow(TableLayoutPanel table, int row, string label, Control editor, bool publicKey)
    {
        var browse = UiHelpers.CreateDialogButton("浏览...");
        browse.MinimumSize = new Size(82, 30);
        browse.Click += (_, _) => BrowsePath(editor, publicKey);
        UiHelpers.AddCompactDialogRow(table, row, label, editor, browse);
    }

    private void LoadDiscoveredKeys(IReadOnlyList<SshPrivateKeyCandidate> discoveredKeys)
    {
        var paths = discoveredKeys
            .Select(item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _privateKeyPath.Items.AddRange(paths.Cast<object>().ToArray());
        var discovery = paths.Length > 0
            ? $"已自动从 {_sshDirectory} 发现 {paths.Length} 个可能的私钥，可直接下拉选择。程序只保存路径，不复制或显示私钥内容。"
            : $"未在 {_sshDirectory} 发现可识别的私钥；仍可手动输入路径或稍后生成密钥。程序只保存路径。";
        _keyDiscoveryNote.Text = discovery
            + Environment.NewLine
            + "登录账号只表示 Git 服务中的网页身份，不等于仓库 Owner。多个不同服务身份可以共用同一套私钥和公钥。";
    }

    private void LoadValues(GitIdentity identity)
    {
        _service.SelectedIndex = _service.Items.Cast<ServiceChoice>().ToList().FindIndex(item =>
            string.Equals(item.Id, identity.ServiceInstanceId, StringComparison.OrdinalIgnoreCase));
        _displayName.Text = identity.DisplayName;
        _username.Text = identity.AccountName;
        _hostAlias.Text = identity.HostAlias;
        _privateKeyPath.Text = identity.PrivateKeyPath;
        _publicKeyPath.Text = identity.PublicKeyPath;
        _comment.Text = identity.EmailOrComment;
        var suggestedPublicPath = identity.PrivateKeyPath + ".pub";
        _lastSuggestedPublicKeyPath = string.Equals(identity.PublicKeyPath, suggestedPublicPath, StringComparison.OrdinalIgnoreCase)
            ? suggestedPublicPath
            : string.Empty;
    }

    private void FillDefaultKeyPaths()
    {
        if (string.IsNullOrWhiteSpace(_hostAlias.Text))
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(_privateKeyPath.Text))
            {
                _privateKeyPath.Text = SshKeyService.CreateDefaultPrivateKeyPath(_sshDirectory, _hostAlias.Text.Trim());
            }

            FillPublicKeyPathFromPrivate();
        }
        catch
        {
            // The domain validator will show the complete error when saving.
        }
    }

    private void FillPublicKeyPathFromPrivate()
    {
        var privateKeyPath = _privateKeyPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            return;
        }

        var suggestedPublicPath = privateKeyPath + ".pub";
        if (string.IsNullOrWhiteSpace(_publicKeyPath.Text)
            || string.Equals(_publicKeyPath.Text.Trim(), _lastSuggestedPublicKeyPath, StringComparison.OrdinalIgnoreCase))
        {
            _publicKeyPath.Text = suggestedPublicPath;
            _lastSuggestedPublicKeyPath = suggestedPublicPath;
        }
    }

    private void BrowsePath(Control target, bool publicKey)
    {
        using var dialog = new OpenFileDialog
        {
            Title = publicKey ? "选择公钥文件" : "选择私钥文件",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(_sshDirectory) ? _sshDirectory : null,
            Filter = publicKey ? "Public key (*.pub)|*.pub|All files (*.*)|*.*" : "All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dialog.FileName;
            if (!publicKey)
            {
                FillPublicKeyPathFromPrivate();
            }
        }
    }

    private void SaveClicked(object? sender, EventArgs eventArgs)
    {
        FillDefaultKeyPaths();
        if (string.IsNullOrWhiteSpace(_displayName.Text)
            || string.IsNullOrWhiteSpace(_username.Text)
            || string.IsNullOrWhiteSpace(_hostAlias.Text)
            || _service.SelectedItem is not ServiceChoice selectedService)
        {
            MessageBox.Show(this, "Git 服务、显示名称、账号和 HostAlias 为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultIdentity = new GitIdentity
        {
            Id = _original.Id,
            ServiceInstanceId = selectedService.Id,
            CreatedAt = _original.CreatedAt,
            DisplayName = _displayName.Text.Trim(),
            AccountName = _username.Text.Trim(),
            HostAlias = _hostAlias.Text.Trim(),
            PrivateKeyPath = _privateKeyPath.Text.Trim(),
            PublicKeyPath = _publicKeyPath.Text.Trim(),
            EmailOrComment = _comment.Text.Trim()
        };
    }

    private sealed record ServiceChoice(string Id, string DisplayText);

    private sealed class CueComboBox : ComboBox
    {
        private const int SetCueBannerMessage = 0x1703;
        private string _cueText = string.Empty;

        public string CueText
        {
            get => _cueText;
            set
            {
                _cueText = value ?? string.Empty;
                ApplyCueBanner();
            }
        }

        protected override void OnHandleCreated(EventArgs eventArgs)
        {
            base.OnHandleCreated(eventArgs);
            ApplyCueBanner();
        }

        private void ApplyCueBanner()
        {
            if (!IsHandleCreated || string.IsNullOrWhiteSpace(_cueText))
            {
                return;
            }

            SendMessage(Handle, SetCueBannerMessage, new IntPtr(1), _cueText);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr parameter, string text);
    }
}
