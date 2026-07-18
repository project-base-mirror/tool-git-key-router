namespace GitKeyRouter.App.Forms;

public sealed class KeyRenameForm : Form
{
    private readonly TextBox _fileName = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "例如：id_ed25519_github_work"
    };

    public KeyRenameForm(string currentFileName, string suggestedFileName)
    {
        Text = "重命名 SSH 密钥文件";
        StartPosition = FormStartPosition.CenterParent;
        Width = 640;
        Height = 245;
        MinimumSize = new Size(540, 220);
        Font = new Font("Segoe UI", 9F);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 4
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        table.Controls.Add(new Label
        {
            Text = "当前文件名",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        table.Controls.Add(new Label
        {
            Text = currentFileName,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        table.Controls.Add(new Label
        {
            Text = "新文件名",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        table.Controls.Add(_fileName, 1, 1);
        table.Controls.Add(new Label
        {
            Text = "只填写文件名，不填写目录。程序会同时重命名私钥、已发现的公钥变体，并更新所有引用同一密钥路径的账户和 SSH managed block。",
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = SystemColors.GrayText
        }, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var preview = new Button { Text = "生成预览", AutoSize = true };
        preview.Click += (_, _) => Save();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(preview);
        table.Controls.Add(buttons, 0, 3);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        AcceptButton = preview;
        CancelButton = cancel;
        _fileName.Text = suggestedFileName;
        _fileName.SelectAll();
    }

    public string NewBaseName { get; private set; } = string.Empty;

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_fileName.Text))
        {
            MessageBox.Show(this, "请输入新的密钥文件名。", "GitKeyRouter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        NewBaseName = _fileName.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
