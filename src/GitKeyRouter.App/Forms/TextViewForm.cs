using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class TextViewForm : Form
{
    public TextViewForm(string title, string content, bool editable = false)
    {
        Text = title;
        UiHelpers.ConfigureDialog(this, 860, 600, resizable: true);

        ContentTextBox = UiHelpers.CreateOutputTextBox(wordWrap: false, scrollBars: ScrollBars.Both);
        ContentTextBox.ReadOnly = !editable;
        ContentTextBox.BackColor = editable ? UiHelpers.Surface : UiHelpers.OutputBackground;
        ContentTextBox.Text = content;
        var close = UiHelpers.CreateDialogButton(editable ? "保存" : "关闭", DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        cancel.Visible = editable;
        var copy = UiHelpers.CreateDialogButton("复制");
        copy.Click += (_, _) => Clipboard.SetText(ContentTextBox.Text);
        var buttons = UiHelpers.CreateDialogButtonBar(close, cancel, copy);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiHelpers.AppBackground };
        body.Controls.Add(UiHelpers.CreateOutputPanel(ContentTextBox));
        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = cancel;
    }

    public TextBox ContentTextBox { get; }
}
