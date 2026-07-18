using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Controls;

internal sealed class OverviewStatusCard : Panel
{
    private const int BadgeWidth = 92;
    private const int ActionGap = 10;

    private readonly Label _description;
    private readonly Label _statusBadge;
    private readonly Button _actionButton;
    private readonly Panel _actionPanel;

    public OverviewStatusCard(string title, string actionText, EventHandler onClick)
    {
        Height = 96;
        MinimumSize = new Size(0, 88);
        BackColor = UiHelpers.Surface;
        Padding = new Padding(18, 12, 18, 12);
        Margin = new Padding(0, 0, 0, 12);
        SetStyle(ControlStyles.ResizeRedraw, true);

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 32,
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = UiHelpers.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _description = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = UiHelpers.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var textPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiHelpers.Surface,
            Margin = Padding.Empty
        };
        textPanel.Controls.Add(_description);
        textPanel.Controls.Add(titleLabel);

        _statusBadge = UiHelpers.CreateOverviewStatusBadge();
        _actionButton = UiHelpers.Button(actionText, onClick);
        _actionButton.AutoSize = false;
        _actionButton.Width = Math.Max(90, _actionButton.PreferredSize.Width);
        _actionButton.Height = 34;
        _actionButton.Margin = Padding.Empty;

        _actionPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = BadgeWidth + ActionGap + _actionButton.Width,
            BackColor = UiHelpers.Surface,
            Margin = Padding.Empty
        };
        _actionPanel.Controls.Add(_statusBadge);
        _actionPanel.Controls.Add(_actionButton);
        _actionPanel.SizeChanged += (_, _) => LayoutActionControls();

        Controls.Add(textPanel);
        Controls.Add(_actionPanel);
        SetState("正在读取实际状态...", "读取中", OverviewStatusKind.Unknown, actionEnabled: false);
        LayoutActionControls();
    }

    public OverviewStatusKind StatusKind { get; private set; }

    public void SetState(
        string description,
        string statusText,
        OverviewStatusKind statusKind,
        bool actionEnabled)
    {
        _description.Text = description;
        StatusKind = statusKind;
        UiHelpers.ApplyOverviewStatusBadge(_statusBadge, statusText, statusKind);
        _actionButton.Enabled = actionEnabled;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        using var border = new Pen(UiHelpers.Border);
        eventArgs.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    private void LayoutActionControls()
    {
        var actionY = Math.Max(0, (_actionPanel.ClientSize.Height - _actionButton.Height) / 2);
        _actionButton.Location = new Point(_actionPanel.ClientSize.Width - _actionButton.Width, actionY);

        var badgeY = Math.Max(0, (_actionPanel.ClientSize.Height - _statusBadge.Height) / 2);
        _statusBadge.Location = new Point(_actionButton.Left - ActionGap - BadgeWidth, badgeY);
    }
}
