namespace GitKeyRouter.App.Controls;

/// <summary>
/// A native-scrolling single column whose items fit the final scrollbar-adjusted viewport.
/// </summary>
internal class AutoWidthFlowLayoutPanel : FlowLayoutPanel
{
    private readonly int _itemWidthInset;
    private bool _resizingItems;

    public AutoWidthFlowLayoutPanel(int itemWidthInset = 0)
    {
        _itemWidthInset = itemWidthInset;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
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
            UpdateScrollExtent();
            base.OnLayout(e);
            // Scrollbar visibility can change ClientSize during base layout.
            // A second layout must measure the resized items before scrolling is used.
            if (ResizeItems())
            {
                UpdateScrollExtent();
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
            var width = Math.Max(1, ClientSize.Width - Padding.Horizontal - item.Margin.Horizontal - _itemWidthInset);
            if (item.Width != width)
            {
                item.Width = width;
                changed = true;
            }
        }

        return changed;
    }

    private void UpdateScrollExtent()
    {
        // FlowLayout's cached bounds omit Padding. Include it in the scroll range so
        // the final item can be fully revealed. PreferredSize also handles hidden items.
        // Remove nonclient borders/scrollbars; only the content height belongs in the range.
        var height = Math.Max(0, GetPreferredSize(Size.Empty).Height - SizeFromClientSize(Size.Empty).Height);
        var minimum = new Size(0, height);
        if (AutoScrollMinSize != minimum)
        {
            AutoScrollMinSize = minimum;
        }
    }
}
