using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

namespace GitKeyRouter.App.Forms;

public sealed class IdentityEditForm : Form
{
    private readonly string _sshDirectory;
    private readonly TextBox _displayName = new();
    private readonly TextBox _username = new();
    private readonly TextBox _hostAlias = new();
    private readonly TextBox _privateKeyPath = new();
    private readonly TextBox _publicKeyPath = new();
    private readonly TextBox _comment = new();
    private readonly GitHubIdentity _original;

    public IdentityEditForm(string sshDirectory, GitHubIdentity? identity = null)
    {
        _sshDirectory = sshDirectory;
        _original = identity ?? new GitHubIdentity();
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

        var note = new Label
        {
            Text = "密钥生成默认不设置 passphrase；程序配置只保存路径，不复制或保存私钥内容。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        };
        table.Controls.Add(note, 1, 6);
        table.SetColumnSpan(note, 2);

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
        LoadValues(_original);
        _hostAlias.Leave += (_, _) => FillDefaultKeyPaths();
    }

    public GitHubIdentity? ResultIdentity { get; private set; }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
    }

    private void AddPathRow(TableLayoutPanel table, int row, string label, TextBox textBox, bool publicKey)
    {
        textBox.Dock = DockStyle.Fill;
        var browse = new Button { Text = "浏览...", Dock = DockStyle.Fill };
        browse.Click += (_, _) => BrowsePath(textBox, publicKey);
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(textBox, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    private void LoadValues(GitHubIdentity identity)
    {
        _displayName.Text = identity.DisplayName;
        _username.Text = identity.GitHubUsername;
        _hostAlias.Text = identity.HostAlias;
        _privateKeyPath.Text = identity.PrivateKeyPath;
        _publicKeyPath.Text = identity.PublicKeyPath;
        _comment.Text = identity.EmailOrComment;
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

            if (string.IsNullOrWhiteSpace(_publicKeyPath.Text))
            {
                _publicKeyPath.Text = _privateKeyPath.Text + ".pub";
            }
        }
        catch
        {
            // The domain validator will show the complete error when saving.
        }
    }

    private void BrowsePath(TextBox target, bool publicKey)
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
            if (!publicKey && string.IsNullOrWhiteSpace(_publicKeyPath.Text))
            {
                _publicKeyPath.Text = dialog.FileName + ".pub";
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

        ResultIdentity = new GitHubIdentity
        {
            Id = _original.Id,
            CreatedAt = _original.CreatedAt,
            DisplayName = _displayName.Text.Trim(),
            GitHubUsername = _username.Text.Trim(),
            HostAlias = _hostAlias.Text.Trim(),
            PrivateKeyPath = _privateKeyPath.Text.Trim(),
            PublicKeyPath = _publicKeyPath.Text.Trim(),
            EmailOrComment = _comment.Text.Trim()
        };
    }
}
