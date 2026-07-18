using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed class DiffPreviewForm : Form
{
    private readonly TextBox _textBox;

    public DiffPreviewForm(string title, string diffText, string executeButtonText = "执行修改")
    {
        Text = title;
        UiHelpers.ConfigureDialog(this, 920, 680, resizable: true);

        _textBox = UiHelpers.CreateOutputTextBox(wordWrap: false, scrollBars: ScrollBars.Both);
        _textBox.Text = diffText;

        var execute = UiHelpers.CreateDialogButton(executeButtonText, DialogResult.OK, primary: true);
        var cancel = UiHelpers.CreateDialogButton("取消", DialogResult.Cancel);
        var copy = UiHelpers.CreateDialogButton("复制变更");
        copy.Click += (_, _) => Clipboard.SetText(_textBox.Text);
        var buttons = UiHelpers.CreateDialogButtonBar(execute, cancel, copy);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiHelpers.AppBackground };
        body.Controls.Add(UiHelpers.CreateOutputPanel(_textBox));
        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = execute;
        CancelButton = cancel;
    }
}
