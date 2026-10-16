using System.Runtime.InteropServices;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Controls;

/// <summary>
/// A single-column navigation list that keeps Windows' native scrolling and accessibility.
/// </summary>
internal sealed class SidebarNavigationPanel : FlowLayoutPanel
{
    private bool _resizingItems;

    public SidebarNavigationPanel()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        BackColor = UiHelpers.SidebarBackground;
        Padding = new Padding(12, 8, 12, 8);
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScrollbarTheme();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyScrollbarTheme();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not null)
        {
            e.Control.Enter += RevealFocusedItem;
        }
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            e.Control.Enter -= RevealFocusedItem;
        }

        base.OnControlRemoved(e);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_resizingItems)
        {
            base.OnLayout(e);
            return;
        }

        _resizingItems = true;
        try
        {
            ResizeItems();
            base.OnLayout(e);
            // Showing or hiding the native vertical scrollbar changes ClientSize.
            // Fit that final width instead of subtracting the scrollbar a second time.
            if (ResizeItems())
            {
                base.OnLayout(e);
            }
        }
        finally
        {
            _resizingItems = false;
        }
    }

    private bool ResizeItems()
    {
        var changed = false;
        foreach (Control item in Controls)
        {
            var width = Math.Max(1, ClientSize.Width - Padding.Horizontal - item.Margin.Horizontal);
            if (item.Width != width)
            {
                item.Width = width;
                changed = true;
            }
        }

        return changed;
    }

    private void RevealFocusedItem(object? sender, EventArgs e)
    {
        if (sender is Control item)
        {
            ScrollControlIntoView(item);
        }
    }

    private void ApplyScrollbarTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        // Use the same native theme name as WinForms dark mode, scoped to the sidebar.
        // Older Windows and accessibility contrast themes retain the system scrollbar.
        var darkTheme = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
            && !SystemInformation.HighContrast;
        var result = SetWindowTheme(Handle, darkTheme ? "DarkMode_Explorer" : null, null);
        if (result < 0 && darkTheme)
        {
            _ = SetWindowTheme(Handle, null, null);
        }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
}
