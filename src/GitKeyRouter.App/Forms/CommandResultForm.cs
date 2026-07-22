using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class CommandResultForm : Form
{
    public CommandResultForm(string title, string content)
    {
        Text = title;
        UiHelpers.ConfigureDialog(this, 920, 650, resizable: true);

        var textBox = UiHelpers.CreateOutputTextBox(wordWrap: false, scrollBars: ScrollBars.Both);
        textBox.Text = content;
        var close = UiHelpers.CreateDialogButton(AppLocalization.T("关闭", "Close"), DialogResult.OK, primary: true);
        var copy = UiHelpers.CreateDialogButton(AppLocalization.T("复制", "Copy"));
        copy.Click += (_, _) => Clipboard.SetText(textBox.Text);
        var buttons = UiHelpers.CreateDialogButtonBar(close, copy);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = UiHelpers.AppBackground };
        body.Controls.Add(UiHelpers.CreateOutputPanel(textBox));
        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = close;
    }

    public static void ShowProcess(IWin32Window owner, string title, ProcessResult result)
    {
        using var form = new CommandResultForm(title, UiHelpers.FormatProcess(result));
        form.ShowDialog(owner);
    }
}
