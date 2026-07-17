namespace GitKeyRouter.App.Forms;

public sealed class TextViewForm : Form
{
    public TextViewForm(string title, string content, bool editable = false)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 860;
        Height = 600;

        ContentTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = !editable,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            Text = content
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var close = new Button { Text = editable ? "保存" : "关闭", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, Visible = editable };
        var copy = new Button { Text = "复制", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(ContentTextBox.Text);
        buttons.Controls.Add(close);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(copy);
        Controls.Add(ContentTextBox);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = cancel;
    }

    public TextBox ContentTextBox { get; }
}
