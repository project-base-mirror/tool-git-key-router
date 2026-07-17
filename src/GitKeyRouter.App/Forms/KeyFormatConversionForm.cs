using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class KeyFormatConversionForm : Form
{
    private readonly ComboBox _targetFormat = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Top
    };
    private readonly CheckBox _overwrite = new()
    {
        Text = "目标格式文件存在时先备份并覆盖",
        AutoSize = true,
        Dock = DockStyle.Top
    };

    public KeyFormatConversionForm(string sourceFormat, string sourcePath)
    {
        Text = "转换公钥格式";
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 260;
        MinimizeBox = false;
        MaximizeBox = false;

        var source = new Label
        {
            Text = $"源格式：{sourceFormat}\r\n源文件：{sourcePath}\r\n\r\n转换结果会以格式后缀写入新文件，原文件保持不变。",
            Dock = DockStyle.Top,
            Height = 92,
            AutoEllipsis = true
        };
        _targetFormat.Items.AddRange(
        [
            new FormatChoice("OpenSSH 公钥（GitHub 可直接使用）", SshPublicKeyExportFormat.OpenSsh),
            new FormatChoice("RFC4716 / SSH2 公钥", SshPublicKeyExportFormat.Rfc4716),
            new FormatChoice("PEM / PKCS8 公钥", SshPublicKeyExportFormat.Pem)
        ]);
        _targetFormat.SelectedIndex = 0;

        var formatLabel = new Label
        {
            Text = "目标格式",
            Dock = DockStyle.Top,
            Height = 24
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var convert = new Button { Text = "转换", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(convert);
        buttons.Controls.Add(cancel);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(_overwrite);
        body.Controls.Add(_targetFormat);
        body.Controls.Add(formatLabel);
        body.Controls.Add(source);
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
