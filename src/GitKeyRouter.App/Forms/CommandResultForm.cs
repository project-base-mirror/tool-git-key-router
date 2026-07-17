using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Forms;

public sealed class CommandResultForm : Form
{
    public CommandResultForm(string title, string content)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 920;
        Height = 650;

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
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
        var close = new Button { Text = "关闭", DialogResult = DialogResult.OK, AutoSize = true };
        var copy = new Button { Text = "复制", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(textBox.Text);
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        Controls.Add(textBox);
        Controls.Add(buttons);
        AcceptButton = close;
    }

    public static void ShowProcess(IWin32Window owner, string title, ProcessResult result)
    {
        using var form = new CommandResultForm(title, UiHelpers.FormatProcess(result));
        form.ShowDialog(owner);
    }
}
