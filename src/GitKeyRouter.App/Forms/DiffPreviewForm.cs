namespace GitKeyRouter.App.Forms;

public sealed class DiffPreviewForm : Form
{
    private readonly TextBox _textBox;

    public DiffPreviewForm(string title, string diffText, string executeButtonText = "执行修改")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 920;
        Height = 680;
        MinimizeBox = false;
        MaximizeBox = true;

        _textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Text = diffText
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var execute = new Button { Text = executeButtonText, DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var copy = new Button { Text = "复制变更", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(_textBox.Text);
        buttons.Controls.Add(execute);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(copy);

        Controls.Add(_textBox);
        Controls.Add(buttons);
        AcceptButton = execute;
        CancelButton = cancel;
    }
}
