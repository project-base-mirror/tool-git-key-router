using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class KeyRenameForm : Form
{
    private readonly TextBox _fileName = new()
    {
        PlaceholderText = "例如：id_ed25519_github_work"
    };

    public KeyRenameForm(string currentFileName, string suggestedFileName)
    {
        Text = "重命名 SSH 密钥文件";
        UiHelpers.ConfigureDialog(this, 640, 230);

        var table = UiHelpers.CreateCompactDialogTable(2, 110);
        var currentName = new Label
        {
            Text = currentFileName,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UiHelpers.AddCompactDialogRow(table, 0, "当前文件名", currentName);
        UiHelpers.AddCompactDialogRow(table, 1, "新文件名", _fileName);
        var note = new Label
        {
            Text = "只填写文件名，不填写目录。程序会同时重命名私钥、已发现的公钥变体，并更新所有引用同一密钥路径的账户和 SSH managed block。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(480, 0)
        };
        UiHelpers.AddCompactDialogContent(table, 2, note, 1);

        var preview = UiHelpers.CreateDialogButton("生成预览", primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        preview.Click += (_, _) => Save();
        var buttons = UiHelpers.CreateDialogButtonBar(preview, cancel);

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
        Controls.Add(buttons);
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
