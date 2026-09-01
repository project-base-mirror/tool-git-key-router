using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Controls;

public sealed class SettingsControl : UserControl, IAsyncRefreshable
{
    private readonly ApplicationServices _services;
    private readonly Action<string> _status;
    private readonly Func<AppLanguage, Task> _changeLanguage;
    private readonly Action<bool> _trayPreferenceApplied;
    private readonly Action<bool> _updatePreferenceApplied;
    private readonly Func<Task> _checkForUpdates;
    private readonly Button _simplifiedChineseButton;
    private readonly Button _englishButton;
    private readonly Button _keepRunningInTrayToggle;
    private readonly Button _checkForUpdatesOnStartupToggle;
    private readonly Button _checkForUpdatesButton;
    private bool _keepRunningInTray = true;
    private bool _checkForUpdatesOnStartup = true;

    public SettingsControl(
        ApplicationServices services,
        Action<string> status,
        Func<AppLanguage, Task> changeLanguage,
        Action<bool> trayPreferenceApplied,
        Action<bool> updatePreferenceApplied,
        Func<Task> checkForUpdates)
    {
        _services = services;
        _status = status;
        _changeLanguage = changeLanguage;
        _trayPreferenceApplied = trayPreferenceApplied;
        _updatePreferenceApplied = updatePreferenceApplied;
        _checkForUpdates = checkForUpdates;

        var header = UiHelpers.CreatePageHeader(
            AppLocalization.T("设置", "Settings"),
            AppLocalization.T("集中管理界面、窗口行为与更新偏好", "Manage appearance, window behavior, and update preferences"),
            AppLocalization.T(
                "这些选项只影响 GitKeyRouter 自身，不会修改 Git、SSH 或仓库路由。\r\n\r\n• 语言切换会立即刷新所有页面。\r\n• 托盘驻留控制关闭和最小化主窗口时的行为。\r\n• 自动更新检查只读取正式发布信息，不会自动安装。",
                "These options affect GitKeyRouter itself and do not change Git, SSH, or repository routes.\r\n\r\n• Language changes refresh every page immediately.\r\n• Tray residence controls what happens when the main window is closed or minimized.\r\n• Automatic update checks only read official release information and never install automatically."));

        _simplifiedChineseButton = CreateOptionButton("SettingsLanguageChineseButton", "简体中文");
        _englishButton = CreateOptionButton("SettingsLanguageEnglishButton", "English");
        _simplifiedChineseButton.Click += async (_, _) => await ChangeLanguageAsync(AppLanguage.SimplifiedChinese);
        _englishButton.Click += async (_, _) => await ChangeLanguageAsync(AppLanguage.English);
        var languageActions = CreateActionPanel(_simplifiedChineseButton, _englishButton);

        _keepRunningInTrayToggle = CreateOptionButton("KeepRunningInTrayToggle", string.Empty, 126);
        _keepRunningInTrayToggle.AccessibleName = AppLocalization.T("系统托盘驻留", "System tray residence");
        _keepRunningInTrayToggle.Click += async (_, _) => await ChangeTrayPreferenceAsync(!_keepRunningInTray);

        _checkForUpdatesOnStartupToggle = CreateOptionButton("CheckForUpdatesOnStartupToggle", string.Empty, 126);
        _checkForUpdatesOnStartupToggle.AccessibleName = AppLocalization.T("启动时检查更新", "Check for updates on startup");
        _checkForUpdatesOnStartupToggle.Click += async (_, _) =>
            await ChangeUpdatePreferenceAsync(!_checkForUpdatesOnStartup);

        _checkForUpdatesButton = CreateOptionButton(
            "CheckForUpdatesButton",
            AppLocalization.T("立即检查更新", "Check now"),
            138);
        ApplyPrimaryStyle(_checkForUpdatesButton);
        _checkForUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        var updateActions = CreateActionPanel(_checkForUpdatesOnStartupToggle, _checkForUpdatesButton);

        var body = new TableLayoutPanel
        {
            Name = "SettingsBody",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UiHelpers.AppBackground,
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(
            CreateSettingCard(
                AppLocalization.T("界面语言", "Interface language"),
                AppLocalization.T("选择后立即刷新主窗口和所有功能页面", "Refresh the shell and every feature page immediately"),
                languageActions),
            0,
            0);
        body.Controls.Add(
            CreateSettingCard(
                AppLocalization.T("系统托盘", "System tray"),
                AppLocalization.T("关闭或最小化主窗口时继续在后台运行", "Keep running when the main window is closed or minimized"),
                _keepRunningInTrayToggle),
            0,
            1);
        body.Controls.Add(
            CreateSettingCard(
                AppLocalization.T("应用更新", "Application updates"),
                AppLocalization.T("启动时检查正式版本，或随时手动检查", "Check official releases at startup or whenever you choose"),
                updateActions),
            0,
            2);

        Controls.Add(body);
        Controls.Add(header);
        ApplyLanguageSelection();
        ApplyToggleStyle(_keepRunningInTrayToggle, _keepRunningInTray);
        ApplyToggleStyle(_checkForUpdatesOnStartupToggle, _checkForUpdatesOnStartup);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var config = await _services.ConfigStore.LoadAsync();
            _keepRunningInTray = config.KeepRunningInTray;
            _checkForUpdatesOnStartup = config.CheckForUpdatesOnStartup;
            ApplyLanguageSelection();
            ApplyToggleStyle(_keepRunningInTrayToggle, _keepRunningInTray);
            ApplyToggleStyle(_checkForUpdatesOnStartupToggle, _checkForUpdatesOnStartup);
        }
        catch (Exception exception)
        {
            _services.Logger.Error("Failed to load application settings.", exception);
            _status(AppLocalization.T("无法读取应用设置", "Could not load application settings"));
        }
    }

    private async Task ChangeLanguageAsync(AppLanguage language)
    {
        if (language == AppLocalization.CurrentLanguage)
        {
            return;
        }

        SetLanguageButtonsEnabled(false);
        await _changeLanguage(language);
        if (!IsDisposed && !Disposing)
        {
            SetLanguageButtonsEnabled(true);
            ApplyLanguageSelection();
        }
    }

    private async Task ChangeTrayPreferenceAsync(bool enabled)
    {
        _keepRunningInTrayToggle.Enabled = false;
        try
        {
            var snapshot = await _services.ConfigStore.LoadSnapshotAsync();
            snapshot.Config.KeepRunningInTray = enabled;
            await _services.ConfigStore.SaveIfUnchangedAsync(snapshot.Config, snapshot.Version);
            _keepRunningInTray = enabled;
            ApplyToggleStyle(_keepRunningInTrayToggle, enabled);
            _trayPreferenceApplied(enabled);
            _status(enabled
                ? AppLocalization.T("已启用系统托盘驻留", "System tray residence enabled")
                : AppLocalization.T("已关闭系统托盘驻留", "System tray residence disabled"));
        }
        catch (Exception exception)
        {
            _services.Logger.Error("Failed to persist tray-residence preference.", exception);
            _status(AppLocalization.T("托盘驻留偏好保存失败", "Could not save the tray-residence preference"));
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _keepRunningInTrayToggle.Enabled = true;
            }
        }
    }

    private async Task ChangeUpdatePreferenceAsync(bool enabled)
    {
        _checkForUpdatesOnStartupToggle.Enabled = false;
        try
        {
            var snapshot = await _services.ConfigStore.LoadSnapshotAsync();
            snapshot.Config.CheckForUpdatesOnStartup = enabled;
            await _services.ConfigStore.SaveIfUnchangedAsync(snapshot.Config, snapshot.Version);
            _checkForUpdatesOnStartup = enabled;
            ApplyToggleStyle(_checkForUpdatesOnStartupToggle, enabled);
            _updatePreferenceApplied(enabled);
            _status(enabled
                ? AppLocalization.T("已启用启动时检查更新", "Startup update checks enabled")
                : AppLocalization.T("已关闭启动时检查更新", "Startup update checks disabled"));
        }
        catch (Exception exception)
        {
            _services.Logger.Error("Failed to persist update-check preference.", exception);
            _status(AppLocalization.T("更新检查偏好保存失败", "Could not save the update-check preference"));
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _checkForUpdatesOnStartupToggle.Enabled = true;
            }
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        _checkForUpdatesButton.Enabled = false;
        try
        {
            await _checkForUpdates();
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _checkForUpdatesButton.Enabled = true;
            }
        }
    }

    private void SetLanguageButtonsEnabled(bool enabled)
    {
        _simplifiedChineseButton.Enabled = enabled;
        _englishButton.Enabled = enabled;
    }

    private void ApplyLanguageSelection()
    {
        ApplyChoiceStyle(_simplifiedChineseButton, AppLocalization.CurrentLanguage == AppLanguage.SimplifiedChinese);
        ApplyChoiceStyle(_englishButton, AppLocalization.CurrentLanguage == AppLanguage.English);
    }

    private static Control CreateSettingCard(string title, string description, Control actions)
    {
        var text = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiHelpers.Surface,
            Margin = Padding.Empty
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI Semibold", 11F),
            ForeColor = UiHelpers.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            UseMnemonic = false
        };
        var descriptionLabel = new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = UiHelpers.TextSecondary,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = false,
            UseMnemonic = false
        };
        text.Controls.Add(descriptionLabel);
        text.Controls.Add(titleLabel);

        actions.Anchor = AnchorStyles.Right;
        actions.Margin = Padding.Empty;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiHelpers.Surface,
            Padding = new Padding(18, 12, 16, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(actions, 1, 0);

        var surface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiHelpers.Surface,
            Margin = Padding.Empty
        };
        surface.Controls.Add(layout);
        var border = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiHelpers.Border,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 12)
        };
        border.Controls.Add(surface);
        return border;
    }

    private static FlowLayoutPanel CreateActionPanel(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = UiHelpers.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.Controls.AddRange(buttons);
        return panel;
    }

    private static Button CreateOptionButton(string name, string text, int width = 112)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            Width = width,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiHelpers.Surface,
            ForeColor = UiHelpers.TextPrimary,
            Font = new Font("Segoe UI Semibold", 9F),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false,
            AutoEllipsis = false,
            UseMnemonic = false
        };
        button.FlatAppearance.BorderColor = UiHelpers.Border;
        button.FlatAppearance.MouseOverBackColor = UiHelpers.AccentSoft;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(214, 226, 247);
        return button;
    }

    private static void ApplyChoiceStyle(Button button, bool selected)
    {
        button.BackColor = selected ? UiHelpers.Accent : UiHelpers.Surface;
        button.ForeColor = selected ? Color.White : UiHelpers.TextPrimary;
        button.FlatAppearance.BorderColor = selected ? UiHelpers.Accent : UiHelpers.Border;
        button.AccessibleRole = AccessibleRole.RadioButton;
        button.AccessibleDescription = selected
            ? AppLocalization.T("当前选中", "Currently selected")
            : AppLocalization.T("点击选择", "Click to select");
    }

    private static void ApplyToggleStyle(Button button, bool enabled)
    {
        button.Text = enabled
            ? AppLocalization.T("已开启", "On")
            : AppLocalization.T("已关闭", "Off");
        button.BackColor = enabled ? UiHelpers.Accent : Color.FromArgb(241, 244, 249);
        button.ForeColor = enabled ? Color.White : UiHelpers.TextSecondary;
        button.FlatAppearance.BorderColor = enabled ? UiHelpers.Accent : UiHelpers.Border;
        button.AccessibleRole = AccessibleRole.CheckButton;
        button.AccessibleDescription = enabled
            ? AppLocalization.T("当前已开启，点击关闭", "Currently on; click to turn off")
            : AppLocalization.T("当前已关闭，点击开启", "Currently off; click to turn on");
    }

    private static void ApplyPrimaryStyle(Button button)
    {
        button.BackColor = UiHelpers.Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = UiHelpers.Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 77, 153);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(31, 66, 132);
    }
}
