using GitKeyRouter.Core.Models;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class KeyFormatConversionForm : Form
{
    private readonly ComboBox _targetFormat = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly CheckBox _overwrite = new()
    {
        Text = "目标格式文件存在时先备份并覆盖",
        AutoSize = true
    };

    public KeyFormatConversionForm(string sourceFormat, string sourcePath)
    {
        Text = "转换公钥格式";
        UiHelpers.ConfigureDialog(this, 620, 250);

        var source = new Label
        {
            Text = $"源格式：{sourceFormat}\r\n源文件：{sourcePath}\r\n\r\n转换结果会以格式后缀写入新文件，原文件保持不变。",
            AutoSize = true,
            AutoEllipsis = true,
            MaximumSize = new Size(560, 0)
        };
        _targetFormat.Items.AddRange(
        [
            new FormatChoice("OpenSSH 公钥（Git 服务可直接使用）", SshPublicKeyExportFormat.OpenSsh),
            new FormatChoice("RFC4716 / SSH2 公钥", SshPublicKeyExportFormat.Rfc4716),
            new FormatChoice("PEM / PKCS8 公钥", SshPublicKeyExportFormat.Pem)
        ]);
        _targetFormat.SelectedIndex = 0;

        var table = UiHelpers.CreateCompactDialogTable(2, 100);
        UiHelpers.AddCompactDialogContent(table, 0, source, 0, 2);
        UiHelpers.AddCompactDialogRow(table, 1, "目标格式", _targetFormat);
        UiHelpers.AddCompactDialogContent(table, 2, _overwrite, 1);

        var convert = UiHelpers.CreateDialogButton("转换", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        var buttons = UiHelpers.CreateDialogButtonBar(convert, cancel);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiHelpers.Surface };
        body.Controls.Add(table);
        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = convert;
        CancelButton = cancel;
    }

    public SshPublicKeyExportFormat SelectedFormat
        => (_targetFormat.SelectedItem as FormatChoice)?.Value ?? SshPublicKeyExportFormat.OpenSsh;

    public bool OverwriteExisting => _overwrite.Checked;

    private sealed record FormatChoice(string Text, SshPublicKeyExportFormat Value)
    {
        public override string ToString() => Text;
    }
}
