using System.Runtime.ExceptionServices;
using System.Reflection;
using GitKeyRouter.App;
using GitKeyRouter.App.Controls;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.App.Updates;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.App.Tests;

public sealed class WinFormsSmokeTests
{
    [Fact]
    public void DialogConstructors_HandleEmptyMissingAndMalformedModels()
        => StaTest.Run(() =>
        {
            var service = new GitServiceInstance
            {
                Id = "office",
                DisplayName = "Office",
                ProviderKind = GitProviderKind.GitLab,
                HostName = "git.example",
                SshUser = "git",
                WebBaseUrl = "https://git.example"
            };
            var identity = new GitIdentity
            {
                Id = "work",
                ServiceInstanceId = service.Id,
                DisplayName = "Work",
                AccountName = "camus",
                HostAlias = "git-work",
                PrivateKeyPath = @"C:\keys\work",
                PublicKeyPath = @"C:\keys\work.pub"
            };
            var profile = new GitProfile
            {
                Id = "profile-work",
                DisplayName = "Work",
                UserName = "Camus",
                UserEmail = "work@example.com",
                DefaultServiceInstanceId = "missing-service",
                DefaultIdentityId = "missing-identity"
            };
            var missingRoute = new RepositoryRoute
            {
                ServiceInstanceId = "missing-service",
                NamespacePath = "company/platform",
                IdentityId = "missing-identity"
            };

            Exercise(new GitServiceEditForm());
            Exercise(new GitServiceEditForm(new GitServiceInstance
            {
                DisplayName = "Invalid low port",
                ProviderKind = (GitProviderKind)999,
                HostName = "low.example",
                SshPort = -10,
                SshUser = "git",
                WebBaseUrl = "https://low.example"
            }));
            Exercise(new GitServiceEditForm(new GitServiceInstance
            {
                DisplayName = "Invalid high port",
                ProviderKind = GitProviderKind.Generic,
                HostName = "high.example",
                SshPort = 70000,
                SshUser = "git",
                WebBaseUrl = "https://high.example"
            }));
            Exercise(new GitProfileEditForm([], []));
            Exercise(new GitProfileEditForm([service], [identity], profile));
            Exercise(new GitProfileRuleEditForm(profile));
            Exercise(new GitProfileRuleEditForm(profile, new GitProfileRule
            {
                ProfileId = profile.Id,
                Kind = (GitProfileRuleKind)999,
                Pattern = "https://git.example/**"
            }));
            Exercise(new IdentityEditForm(Path.GetTempPath(), []));
            Exercise(new IdentityEditForm(Path.GetTempPath(), [service], new GitIdentity
            {
                ServiceInstanceId = "missing-service",
                DisplayName = "Missing",
                AccountName = "missing",
                HostAlias = "missing-alias"
            }));
            Exercise(new OwnerRouteEditForm([], [], missingRoute));
            Exercise(new OwnerRouteEditForm([service], [identity], new RepositoryRoute
            {
                ServiceInstanceId = service.Id,
                NamespacePath = "company/platform",
                IdentityId = identity.Id
            }));
            Exercise(new KeyFormatConversionForm("OpenSSH", @"C:\keys\work.pub"));
            Exercise(new KeyRenameForm("id_work", "id_work_renamed"));
            Exercise(new DiffPreviewForm("Diff", "--- before\n+++ after"));
            Exercise(new CommandResultForm("Result", "output"));
            Exercise(new TextViewForm("View", "content"));
            Exercise(new TextViewForm("Edit", "content", editable: true));
            Exercise(new PortableBackupPasswordForm(confirmPassword: false));
            Exercise(new PortableBackupPasswordForm(confirmPassword: true));
        });

    [Fact]
    public void PageConstructors_AndProfileSplitter_HandleExtremeSizes()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            Exercise(new OverviewControl(services, _ => { }, _ => Task.CompletedTask));
            Exercise(new GitServicesControl(services, _ => { }));
            Exercise(new IdentitiesControl(services, _ => { }));
            Exercise(new OwnerRoutesControl(services, _ => { }));
            Exercise(new SshConfigControl(services, _ => { }));
            Exercise(new GitRewritesControl(services, _ => { }));
            Exercise(new DiagnosticsControl(services, _ => { }));
            Exercise(new BackupControl(services, _ => { }));
            Exercise(CreateSettingsControl(services));

            using var profiles = new GitProfilesControl(services, _ => { });
            _ = profiles.Handle;
            foreach (var size in new[]
                     {
                         new Size(80, 60),
                         new Size(1200, 720),
                         new Size(100, 80),
                         new Size(900, 500)
                     })
            {
                profiles.Size = size;
                profiles.PerformLayout();
                Application.DoEvents();
                var split = Assert.Single(profiles.Controls.OfType<SplitContainer>());
                var available = Math.Max(0, split.ClientSize.Height - split.SplitterWidth);
                if (available > 0)
                {
                    Assert.InRange(split.SplitterDistance, 0, available);
                }
            }

            Exercise(new MainForm(services), new Size(1024, 680), new Size(1280, 820));
        });

    [Fact]
    public void BackupPageExposesPortableEncryptedImportAndExport()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            using var page = new BackupControl(services, _ => { });
            _ = page.Handle;

            Assert.Single(
                Descendants<Button>(page),
                button => button.Name == "ExportPortableBackupButton");
            Assert.Single(
                Descendants<Button>(page),
                button => button.Name == "ImportPortableBackupButton");

            using var password = new PortableBackupPasswordForm(confirmPassword: true);
            _ = password.Handle;
            Assert.Single(
                Descendants<TextBox>(password),
                textBox => textBox.Name == "PortableBackupPasswordTextBox" && textBox.UseSystemPasswordChar);
            Assert.Single(
                Descendants<TextBox>(password),
                textBox => textBox.Name == "PortableBackupPasswordConfirmTextBox" && textBox.UseSystemPasswordChar);
        });

    [Fact]
    public void SharedPageLayout_KeepsLongContentAccessibleAtMinimumViewport()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            AppLocalization.SetLanguage(AppLanguage.English);
            var pages = new UserControl[]
            {
                new OverviewControl(services, _ => { }, _ => Task.CompletedTask),
                new GitServicesControl(services, _ => { }),
                new IdentitiesControl(services, _ => { }),
                new GitProfilesControl(services, _ => { }),
                new OwnerRoutesControl(services, _ => { }),
                new SshConfigControl(services, _ => { }),
                new GitRewritesControl(services, _ => { }),
                new DiagnosticsControl(services, _ => { }),
                new BackupControl(services, _ => { }),
                CreateSettingsControl(services)
            };

            try
            {
                foreach (var page in pages)
                {
                    _ = page.Handle;
                    page.Size = new Size(640, 520);
                    page.PerformLayout();
                    Application.DoEvents();

                    var header = Assert.Single(page.Controls.Cast<Control>(), control => control.Name == "PageHeader");
                    Assert.True(header.Height >= 72);
                    Assert.InRange(header.Bottom, 1, page.ClientSize.Height);

                    var toolbar = page.Controls.Cast<Control>()
                        .SingleOrDefault(control => control.Name == "PageToolbar");
                    if (toolbar is not null)
                    {
                        Assert.True(toolbar.Height >= 52);
                        Assert.False(header.Bounds.IntersectsWith(toolbar.Bounds));
                        Assert.InRange(toolbar.Bottom, 1, page.ClientSize.Height);
                    }

                    foreach (var grid in Descendants<DataGridView>(page))
                    {
                        Assert.Equal(DataGridViewAutoSizeColumnsMode.None, grid.AutoSizeColumnsMode);
                        Assert.Equal(ScrollBars.Both, grid.ScrollBars);
                    }
                }

                using var narrowHeader = UiHelpers.CreatePageHeader(
                    "Git URL Rewrites and Repository Routing Settings",
                    "Map SSH identities by service, owner, or repository; repository routes override owner routes, which override service routes.",
                    "Help text");
                narrowHeader.Size = new Size(320, 72);
                _ = narrowHeader.Handle;
                narrowHeader.PerformLayout();
                Application.DoEvents();
                var title = Assert.Single(
                    Descendants<Label>(narrowHeader),
                    label => label.Name == "PageHeaderTitle");
                var subtitle = Assert.Single(
                    Descendants<Label>(narrowHeader),
                    label => label.Name == "PageHeaderSubtitle");
                var help = Assert.Single(
                    Descendants<Button>(narrowHeader),
                    button => button.Name == "PageHelpButton");
                Assert.True(narrowHeader.Height > 72);
                var titleBounds = narrowHeader.RectangleToClient(
                    title.RectangleToScreen(title.ClientRectangle));
                var subtitleBounds = narrowHeader.RectangleToClient(
                    subtitle.RectangleToScreen(subtitle.ClientRectangle));
                var helpBounds = narrowHeader.RectangleToClient(
                    help.RectangleToScreen(help.ClientRectangle));
                Assert.True(narrowHeader.ClientRectangle.Contains(titleBounds));
                Assert.True(narrowHeader.ClientRectangle.Contains(subtitleBounds));
                Assert.False(titleBounds.IntersectsWith(helpBounds));
                Assert.False(subtitleBounds.IntersectsWith(helpBounds));
                Assert.False(titleBounds.IntersectsWith(subtitleBounds));

                using var gridHost = new Form
                {
                    ClientSize = new Size(940, 260),
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-32000, -32000),
                    ShowInTaskbar = false
                };
                using var sampleGrid = UiHelpers.CreateGrid();
                sampleGrid.Location = new Point(12, 12);
                sampleGrid.Size = new Size(900, 220);
                gridHost.Controls.Add(sampleGrid);
                gridHost.Show();
                sampleGrid.DataSource = new[] { new { Name = "Example", Details = "Short value" } };
                sampleGrid.PerformLayout();
                Application.DoEvents();
                var roomyColumns = sampleGrid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Visible)
                    .ToList();
                Assert.Single(roomyColumns, column => column.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill);
                Assert.InRange(
                    sampleGrid.DisplayRectangle.Width - roomyColumns.Sum(column => column.Width),
                    -4,
                    24);

                gridHost.ClientSize = new Size(260, 260);
                sampleGrid.DataSource = new[] { new { Name = "Example", Details = new string('x', 1000) } };
                sampleGrid.PerformLayout();
                Application.DoEvents();
                var constrainedColumns = sampleGrid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Visible)
                    .ToList();
                Assert.All(
                    constrainedColumns,
                    column => Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode));
                Assert.True(constrainedColumns.Sum(column => column.Width) > sampleGrid.DisplayRectangle.Width);

                using var main = new MainForm(services);
                _ = main.Handle;
                var contentPanel = Assert.Single(
                    Descendants<Panel>(main),
                    panel => panel.Name == "MainContentPanel");
                Assert.True(contentPanel.AutoScroll);
                Assert.True(contentPanel.AutoScrollMinSize.Width >= 640 + contentPanel.Padding.Horizontal);
                Assert.True(contentPanel.AutoScrollMinSize.Height >= 520 + contentPanel.Padding.Vertical);
            }
            finally
            {
                foreach (var page in pages)
                {
                    page.Dispose();
                }

                AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            }
        });

    [Fact]
    public void MainPagesExposeHelpAndSettingsPageOwnsPreferences()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            AppLocalization.SetLanguage(AppLanguage.English);
            var pages = new UserControl[]
            {
                new OverviewControl(services, _ => { }, _ => Task.CompletedTask),
                new GitServicesControl(services, _ => { }),
                new IdentitiesControl(services, _ => { }),
                new GitProfilesControl(services, _ => { }),
                new OwnerRoutesControl(services, _ => { }),
                new SshConfigControl(services, _ => { }),
                new GitRewritesControl(services, _ => { }),
                new DiagnosticsControl(services, _ => { }),
                new BackupControl(services, _ => { }),
                CreateSettingsControl(services)
            };

            try
            {
                foreach (var page in pages)
                {
                    _ = page.Handle;
                    Assert.Single(Descendants<Button>(page), button => button.Name == "PageHelpButton");
                }

                using var main = new MainForm(services);
                main.Size = main.MinimumSize;
                _ = main.Handle;
                main.PerformLayout();
                Application.DoEvents();
                var settings = Assert.IsType<SettingsControl>(pages[^1]);
                Assert.Empty(Descendants<ComboBox>(main));
                Assert.Empty(Descendants<ComboBox>(settings));
                Assert.Empty(Descendants<CheckBox>(settings));
                Assert.Single(
                    Descendants<Button>(settings),
                    button => button.Name == "SettingsLanguageChineseButton");
                Assert.Single(
                    Descendants<Button>(settings),
                    button => button.Name == "SettingsLanguageEnglishButton");
                Assert.Single(
                    Descendants<Button>(settings),
                    button => button.Name == "CheckForUpdatesOnStartupToggle");
                Assert.Single(
                    Descendants<Button>(settings),
                    button => button.Name == "KeepRunningInTrayToggle");
                Assert.Single(
                    Descendants<Button>(settings),
                    button => button.Name == "CheckForUpdatesButton");
                var navigation = Assert.Single(
                    Descendants<FlowLayoutPanel>(main),
                    panel => panel.Name == "SidebarNavigation");
                Assert.True(navigation.AutoScroll);
                Assert.False(navigation.VerticalScroll.Visible);
                Assert.DoesNotContain(
                    Descendants<Label>(main),
                    label => label.Text is "NAVIGATION" or "导航"
                        || label.Text.Contains("Click the brand", StringComparison.OrdinalIgnoreCase)
                        || label.Text.Contains("点击左上角品牌", StringComparison.Ordinal));
                var trayMenuField = typeof(MainForm).GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic);
                var trayMenu = Assert.IsType<ContextMenuStrip>(trayMenuField?.GetValue(main));
                Assert.Contains(
                    trayMenu.Items.OfType<ToolStripMenuItem>(),
                    item => item.Name == "TrayCheckUpdatesMenuItem");
                Assert.Contains("     Git Services", Descendants<Button>(main).Select(button => button.Text));
                Assert.Contains("⚙   Settings", Descendants<Button>(main).Select(button => button.Text));
            }
            finally
            {
                foreach (var page in pages)
                {
                    page.Dispose();
                }

                AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            }
        });

    [Fact]
    public void SidebarNavigation_KeepsOverflowReachableAndRemovesScrollbarsWhenItFits()
        => StaTest.Run(() =>
        {
            using var main = new MainForm(AppBootstrapper.CreateServices());
            var navigation = Assert.Single(
                Descendants<FlowLayoutPanel>(main),
                panel => panel.Name == "SidebarNavigation");
            var buttons = navigation.Controls.OfType<Button>().ToArray();
            Assert.Equal(10, buttons.Length);

            // Host only the navigation: showing MainForm would run tool detection and updates.
            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            host.Controls.Add(navigation);
            host.Show();

            foreach (var scale in new[] { 1F, 1.25F, 1.5F, 2F })
            {
                // Exercise the layout with the dimensions produced by common DPI scales.
                // Native per-monitor DPI and visual theme switching also need a Windows UI check.
                navigation.Padding = new Padding(
                    (int)(12 * scale), (int)(8 * scale), (int)(12 * scale), (int)(8 * scale));
                foreach (var button in buttons)
                {
                    button.Height = (int)(42 * scale);
                    button.Margin = new Padding(0, 0, 0, (int)(4 * scale));
                }

                host.ClientSize = new Size((int)(260 * scale), (int)(220 * scale));
                navigation.PerformLayout();
                Application.DoEvents();

                Assert.True(navigation.VerticalScroll.Visible);
                Assert.False(navigation.HorizontalScroll.Visible);
                Assert.All(buttons, button =>
                    Assert.Equal(navigation.ClientSize.Width - navigation.Padding.Horizontal, button.Width));

                Assert.True(buttons[^1].Focus());
                Application.DoEvents();
                Assert.True(navigation.AutoScrollPosition.Y < 0);
                Assert.True(navigation.ClientRectangle.Contains(buttons[^1].Bounds));
                Assert.True(buttons[0].Focus());
                Application.DoEvents();
                Assert.True(navigation.ClientRectangle.Contains(buttons[0].Bounds));

                if (SystemInformation.MouseWheelScrollLines != 0)
                {
                    var mouseWheel = typeof(ScrollableControl).GetMethod(
                        "OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(mouseWheel);
                    mouseWheel.Invoke(navigation, [new MouseEventArgs(MouseButtons.None, 0, 20, 20, -120)]);
                    Assert.True(navigation.AutoScrollPosition.Y < 0);
                }

                // Recreate the native handle, then repeat scrolling so theme setup cannot break it.
                var recreateHandle = typeof(Control).GetMethod(
                    "RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(recreateHandle);
                recreateHandle.Invoke(navigation, null);
                navigation.ScrollControlIntoView(buttons[^1]);
                Application.DoEvents();
                Assert.True(navigation.ClientRectangle.Contains(buttons[^1].Bounds));
                Assert.False(navigation.HorizontalScroll.Visible);

                host.ClientSize = new Size((int)(260 * scale), (int)(700 * scale));
                navigation.PerformLayout();
                Application.DoEvents();
                Assert.False(navigation.VerticalScroll.Visible);
                Assert.False(navigation.HorizontalScroll.Visible);
                Assert.Equal(Point.Empty, navigation.AutoScrollPosition);
                Assert.All(buttons, button =>
                    Assert.True(navigation.ClientRectangle.Contains(button.Bounds)));
            }
        });

    [Fact]
    public void OverviewCards_UseAvailableWidthWithAndWithoutVerticalOverflow()
        => StaTest.Run(() =>
        {
            using var host = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false
            };
            using var page = new OverviewControl(
                AppBootstrapper.CreateServices(), _ => { }, _ => Task.CompletedTask)
            {
                Dock = DockStyle.Fill
            };
            host.Controls.Add(page);
            host.Show();
            var cards = Assert.Single(page.Controls.OfType<FlowLayoutPanel>(), panel => panel.AutoScroll);

            foreach (var height in new[] { 520, 900, 520 })
            {
                host.ClientSize = new Size(640, height);
                page.PerformLayout();
                Application.DoEvents();

                Assert.Equal(height == 520, cards.VerticalScroll.Visible);
                Assert.False(cards.HorizontalScroll.Visible);
                Assert.Equal(6, cards.Controls.Count);
                Assert.All(cards.Controls.Cast<Control>(), card =>
                    Assert.Equal(cards.ClientSize.Width - cards.Padding.Horizontal - 2, card.Width));
            }
        });

    [Fact]
    public void UpdateDialog_OffersInstallerAndPortableActions()
        => StaTest.Run(() =>
        {
            var previousLanguage = AppLocalization.CurrentLanguage;
            AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            try
            {
                var release = new UpdateReleaseInfo(
                    "v9.8.7",
                    new Version(9, 8, 7, 0),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/tag/v9.8.7"),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/download/v9.8.7/GitKeyRouter-v9.8.7-win-x64-framework-dependent.zip"),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/download/v9.8.7/GitKeyRouter-v9.8.7-win-x64-portable.zip"),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/download/v9.8.7/GitKeyRouter-v9.8.7-win-x64-framework-dependent-setup.msi"),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/download/v9.8.7/GitKeyRouter-v9.8.7-win-x64-setup.msi"),
                    new Uri("https://github.com/project-base-mirror/tool-git-key-router/releases/download/v9.8.7/SHA256SUMS.txt"),
                    "release notes",
                    UpdateReleaseSource.PagesManifest);

                using var installer = new UpdateDialog(
                    new Version(9, 8, 6, 0),
                    release,
                    UpdatePackageKind.InstallerSelfContained,
                    allowVerifiedInstaller: true);
                _ = installer.Handle;
                var installerPrimary = Assert.Single(
                    Descendants<Button>(installer),
                    button => button.Name == "UpdatePrimaryActionButton");
                Assert.Equal("下载并安装", installerPrimary.Text);
                Assert.Same(installerPrimary, installer.AcceptButton);
                Assert.Single(Descendants<Button>(installer), button => button.Name == "UpdateReleaseButton");
                Assert.Single(Descendants<Button>(installer), button => button.Name == "UpdateLaterButton");

                using var portable = new UpdateDialog(
                    new Version(9, 8, 6, 0),
                    release,
                    UpdatePackageKind.PortableSelfContained,
                    allowVerifiedInstaller: false);
                _ = portable.Handle;
                var portablePrimary = Assert.Single(
                    Descendants<Button>(portable),
                    button => button.Name == "UpdatePrimaryActionButton");
                Assert.Equal("打开匹配下载", portablePrimary.Text);
                Assert.Contains("便携版", portablePrimary.AccessibleDescription, StringComparison.Ordinal);
            }
            finally
            {
                AppLocalization.SetLanguage(previousLanguage);
            }
        });

    [Fact]
    public void SshConfigUsesStyledViewSelectorInsteadOfNativeTabs()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            using var page = new SshConfigControl(services, _ => { });
            page.Size = new Size(1000, 700);
            _ = page.Handle;
            page.PerformLayout();
            Application.DoEvents();

            Assert.Empty(Descendants<TabControl>(page));
            var selector = Assert.Single(
                Descendants<FlowLayoutPanel>(page),
                panel => panel.Name == "SshConfigViewSelector");
            var host = Assert.Single(
                Descendants<Panel>(page),
                panel => panel.Name == "SshConfigViewHost");
            var buttons = selector.Controls.Cast<Button>().ToList();
            Assert.Equal(3, buttons.Count);

            foreach (var button in buttons)
            {
                var view = Assert.IsAssignableFrom<Control>(button.Tag);
                button.PerformClick();
                Application.DoEvents();

                Assert.Equal(UiHelpers.Accent, button.BackColor);
                Assert.Equal(Color.White, button.ForeColor);
                Assert.True(view.Visible);
                Assert.Equal(0, host.Controls.GetChildIndex(view));
                Assert.Single(buttons, item => item.BackColor == UiHelpers.Accent);
                Assert.Single(host.Controls.Cast<Control>(), item => item.Visible);
            }
        });

    [Fact]
    public void SidebarBrandTitleRemainsFullyVisibleAtMinimumWindowSize()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            try
            {
                foreach (var language in new[] { AppLanguage.SimplifiedChinese, AppLanguage.English })
                {
                    AppLocalization.SetLanguage(language);
                    using var main = new MainForm(services);
                    main.Size = main.MinimumSize;
                    _ = main.Handle;
                    main.PerformLayout();
                    Application.DoEvents();

                    var sidebar = Assert.Single(
                        Descendants<Panel>(main),
                        panel => panel.Name == "MainSidebar");
                    var title = Assert.Single(
                        Descendants<Label>(main),
                        label => label.Name == "SidebarBrandTitle");
                    var preferredWidth = TextRenderer.MeasureText(
                        title.Text,
                        title.Font,
                        Size.Empty,
                        TextFormatFlags.NoPadding).Width;

                    Assert.True(sidebar.Width >= 260);
                    Assert.True(
                        title.ClientSize.Width >= preferredWidth,
                        $"Brand title width {title.ClientSize.Width}px is smaller than preferred {preferredWidth}px.");
                    Assert.Equal("GitKeyRouter", title.Text);
                    Assert.False(title.AutoEllipsis);
                }
            }
            finally
            {
                AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            }
        });

    [Fact]
    public void MainWindow_UsesBrandedApplicationIcon()
        => StaTest.Run(() =>
        {
            var services = AppBootstrapper.CreateServices();
            using var main = new MainForm(services);

            Assert.NotNull(main.Icon);
            using var bitmap = main.Icon.ToBitmap();
            IconAssertions.AssertGitRouterMark(bitmap);
        });

    [Fact]
    public void SingleInstanceGuard_AllowsOnlyOneOwnerForTheSameName()
    {
        var mutexName = $@"Local\GitKeyRouter.Tests.{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.TryAcquire(mutexName);
        using var second = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);

        first.Dispose();
        using var third = SingleInstanceGuard.TryAcquire(mutexName);
        Assert.True(third.IsPrimaryInstance);
    }

    [Fact]
    public void SingleInstanceGuard_SecondarySignalsPrimaryActivation()
    {
        var mutexName = $@"Local\GitKeyRouter.Tests.{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim();
        using var first = SingleInstanceGuard.TryAcquire(mutexName);
        first.StartListening(() => activated.Set());

        using var second = SingleInstanceGuard.TryAcquire(mutexName);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
    }

    private static SettingsControl CreateSettingsControl(ApplicationServices services)
        => new(
            services,
            _ => { },
            _ => Task.CompletedTask,
            _ => { },
            _ => { },
            () => Task.CompletedTask);

    private static void Exercise(Control control, params Size[] sizes)
    {
        using (control)
        {
            _ = control.Handle;
            foreach (var size in sizes.Length == 0 ? [new Size(800, 600)] : sizes)
            {
                control.Size = size;
                control.PerformLayout();
                Application.DoEvents();
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static class StaTest
    {
        public static void Run(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            })
            {
                IsBackground = true,
                Name = "GitKeyRouter WinForms smoke test"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("The WinForms smoke test did not finish within 30 seconds.");
            }

            if (error is not null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}
