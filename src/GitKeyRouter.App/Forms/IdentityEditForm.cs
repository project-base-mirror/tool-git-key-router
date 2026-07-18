using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;
using System.Runtime.InteropServices;

namespace GitKeyRouter.App.Forms;

public sealed class IdentityEditForm : Form
{
    private readonly string _sshDirectory;
    private readonly TextBox _displayName = new() { PlaceholderText = "例如：个人 GitHub" };
    private readonly TextBox _username = new() { PlaceholderText = "例如：octocat" };
    private readonly TextBox _hostAlias = new() { PlaceholderText = "例如：github-personal" };
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
        GitIdentity? identity = null,
        IReadOnlyList<SshPrivateKeyCandidate>? discoveredKeys = null)
    {
        _sshDirectory = sshDirectory;
        _original = identity ?? new GitIdentity();
        Text = identity is null ? "新建 GitHub 身份" : "编辑 GitHub 身份";
        StartPosition = FormStartPosition.CenterParent;
        Width = 760;
        Height = 430;
        MinimizeBox = false;
        MaximizeBox = false;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 7,
            Padding = new Padding(14),
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        AddRow(table, 0, "显示名称", _displayName);
        AddRow(table, 1, "GitHub 用户名", _username);
        AddRow(table, 2, "HostAlias", _hostAlias);
        AddPathRow(table, 3, "私钥路径", _privateKeyPath, false);
        AddPathRow(table, 4, "公钥路径", _publicKeyPath, true);
        AddRow(table, 5, "注释 / Email", _comment);

        _keyDiscoveryNote.Dock = DockStyle.Fill;
        _keyDiscoveryNote.AutoSize = true;
        _keyDiscoveryNote.ForeColor = SystemColors.GrayText;
        table.Controls.Add(_keyDiscoveryNote, 1, 6);
        table.SetColumnSpan(_keyDiscoveryNote, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var save = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += SaveClicked;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(table);
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
    {
        control.Dock = DockStyle.Fill;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
    }

    private void AddPathRow(TableLayoutPanel table, int row, string label, Control editor, bool publicKey)
    {
        editor.Dock = DockStyle.Fill;
        var browse = new Button { Text = "浏览...", Dock = DockStyle.Fill };
        browse.Click += (_, _) => BrowsePath(editor, publicKey);
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(editor, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    private void LoadDiscoveredKeys(IReadOnlyList<SshPrivateKeyCandidate> discoveredKeys)
    {
        var paths = discoveredKeys
            .Select(item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _privateKeyPath.Items.AddRange(paths.Cast<object>().ToArray());
        _keyDiscoveryNote.Text = paths.Length > 0
            ? $"已自动从 {_sshDirectory} 发现 {paths.Length} 个可能的私钥，可直接下拉选择。程序只保存路径，不复制或显示私钥内容。"
            : $"未在 {_sshDirectory} 发现可识别的私钥；仍可手动输入路径或稍后生成密钥。程序只保存路径。";
    }

    private void LoadValues(GitIdentity identity)
    {
        _displayName.Text = identity.DisplayName;
        _username.Text = identity.GitHubUsername;
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
            || string.IsNullOrWhiteSpace(_hostAlias.Text))
        {
            MessageBox.Show(this, "显示名称、GitHub 用户名和 HostAlias 为必填项。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        ResultIdentity = new GitIdentity
        {
            Id = _original.Id,
            ServiceInstanceId = _original.ServiceInstanceId,
            CreatedAt = _original.CreatedAt,
            DisplayName = _displayName.Text.Trim(),
            GitHubUsername = _username.Text.Trim(),
            HostAlias = _hostAlias.Text.Trim(),
            PrivateKeyPath = _privateKeyPath.Text.Trim(),
            PublicKeyPath = _publicKeyPath.Text.Trim(),
            EmailOrComment = _comment.Text.Trim()
        };
    }

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
