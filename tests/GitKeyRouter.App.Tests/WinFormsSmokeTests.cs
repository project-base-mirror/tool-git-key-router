using System.Runtime.ExceptionServices;
using GitKeyRouter.App;
using GitKeyRouter.App.Controls;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;
using GitKeyRouter.Core.Services;

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
                new BackupControl(services, _ => { })
            };

            try
            {
                foreach (var page in pages)
                {
                    _ = page.Handle;
                    foreach (var size in new[] { new Size(720, 520), new Size(900, 500) })
                    {
                        page.Size = size;
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
                            Assert.All(
                                toolbar.Controls.Cast<Control>(),
                                control => Assert.InRange(control.Bottom, 1, toolbar.ClientSize.Height));
                        }

                        foreach (var pageGrid in Descendants<DataGridView>(page))
                        {
                            Assert.Equal(DataGridViewAutoSizeColumnsMode.None, pageGrid.AutoSizeColumnsMode);
                            Assert.Equal(ScrollBars.Both, pageGrid.ScrollBars);
                        }
                    }
                }

                using var narrowHeader = UiHelpers.CreatePageHeader(
                    "Repository Routes",
                    "Map SSH identities by service, owner, or repository; repository routes override owner routes, which override service routes.",
                    "Help text");
                narrowHeader.Size = new Size(360, 72);
                _ = narrowHeader.Handle;
                narrowHeader.PerformLayout();
                Application.DoEvents();
                var subtitle = Assert.Single(
                    Descendants<Label>(narrowHeader),
                    label => label.Name == "PageHeaderSubtitle");
                var help = Assert.Single(
                    Descendants<Button>(narrowHeader),
                    button => button.Name == "PageHelpButton");
                Assert.True(narrowHeader.Height > 72);
                var subtitleBounds = narrowHeader.RectangleToClient(
                    subtitle.RectangleToScreen(subtitle.ClientRectangle));
                var helpBounds = narrowHeader.RectangleToClient(
                    help.RectangleToScreen(help.ClientRectangle));
                Assert.True(narrowHeader.ClientRectangle.Contains(subtitleBounds));
                Assert.False(subtitleBounds.IntersectsWith(helpBounds));

                using var sampleGrid = UiHelpers.CreateGrid();
                sampleGrid.DataSource = new[] { new { Name = "Example", Details = new string('x', 1000) } };
                _ = sampleGrid.Handle;
                sampleGrid.PerformLayout();
                Application.DoEvents();
                Assert.All(
                    sampleGrid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible),
                    column =>
                    {
                        Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode);
                        Assert.InRange(column.Width, 90, 420);
                    });

                using var main = new MainForm(services);
                _ = main.Handle;
                var contentPanel = Assert.Single(
                    Descendants<Panel>(main),
                    panel => panel.Name == "MainContentPanel");
                Assert.True(contentPanel.AutoScroll);
                Assert.True(contentPanel.AutoScrollMinSize.Width >= 720 + contentPanel.Padding.Horizontal);
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

    [Theory]
    [InlineData(96, 90, 420)]
    [InlineData(120, 113, 525)]
    [InlineData(144, 135, 630)]
    [InlineData(192, 180, 840)]
    public void GridColumnWidthRanges_ScaleForHighDpi(
        int deviceDpi,
        int expectedMinimum,
        int expectedMaximum)
    {
        var scaled = UiHelpers.ScaleGridColumnWidthRange(
            UiHelpers.DefaultGridColumnWidthRange,
            deviceDpi);

        Assert.Equal(expectedMinimum, scaled.MinimumWidth);
        Assert.Equal(expectedMaximum, scaled.MaximumWidth);
    }

    [Fact]
    public void SharedGridBinding_RecalculatesDeterministicWidthsAcrossDataChanges()
        => StaTest.Run(() =>
        {
            using var grid = UiHelpers.CreateGrid();
            using var host = new UserControl { Size = new Size(360, 220) };
            host.Controls.Add(grid);
            _ = host.Handle;
            _ = grid.Handle;
            host.PerformLayout();
            Application.DoEvents();

            BindGrid(grid,
            [
                new GridLayoutRow
                {
                    Name = new string('n', 300),
                    Details = new string('x', 1000)
                }
            ]);
            AssertGridLayout(grid);
            var longDetailsWidth = GridColumn(grid, nameof(GridLayoutRow.Details)).Width;
            var defaultRange = UiHelpers.ScaleGridColumnWidthRange(
                UiHelpers.DefaultGridColumnWidthRange,
                grid.DeviceDpi);
            Assert.Equal(defaultRange.MaximumWidth, longDetailsWidth);
            Assert.True(VisibleColumnWidth(grid) > grid.ClientSize.Width);

            BindGrid(grid, []);
            AssertGridLayout(grid);

            BindGrid(grid,
            [
                new GridLayoutRow
                {
                    Name = "Short",
                    Details = "Small"
                }
            ]);
            AssertGridLayout(grid);
            var shortDetailsWidth = GridColumn(grid, nameof(GridLayoutRow.Details)).Width;
            Assert.True(shortDetailsWidth < longDetailsWidth);

            BindGrid(grid,
            [
                new GridLayoutRow
                {
                    Name = new string('n', 300),
                    Details = new string('x', 1000)
                }
            ]);
            AssertGridLayout(grid);
            Assert.Equal(longDetailsWidth, GridColumn(grid, nameof(GridLayoutRow.Details)).Width);
        });

    [Fact]
    public void GitServicesRefresh_KeepsLongColumnsBoundedAndScrollableAcrossRebinding()
        => StaTest.Run(() =>
        {
            AppLocalization.SetLanguage(AppLanguage.English);
            try
            {
                var configStore = new TestAppConfigStore(CreateGitServiceLayoutConfig(longText: true));
                var services = CreateGitServicesOnlyApplicationServices(configStore);
                using var page = new GitServicesControl(services, _ => { });
                _ = page.Handle;

                page.Size = new Size(720, 520);
                page.PerformLayout();
                page.RefreshAsync().GetAwaiter().GetResult();
                Application.DoEvents();

                var grid = Assert.Single(Descendants<DataGridView>(page));
                var overrides = GitServiceGridColumnWidths();
                AssertGridLayout(grid, overrides);
                var longUrlWidth = GridColumn(grid, "Web地址").Width;
                var scaledUrlRange = UiHelpers.ScaleGridColumnWidthRange(overrides["Web地址"], grid.DeviceDpi);
                Assert.Equal(scaledUrlRange.MaximumWidth, longUrlWidth);
                Assert.True(VisibleColumnWidth(grid) > grid.ClientSize.Width);

                configStore.Config = CreateGitServiceLayoutConfig(longText: false);
                page.Size = new Size(900, 500);
                page.PerformLayout();
                page.RefreshAsync().GetAwaiter().GetResult();
                Application.DoEvents();

                AssertGridLayout(grid, overrides);
                Assert.True(GridColumn(grid, "Web地址").Width < longUrlWidth);
                Assert.True(VisibleColumnWidth(grid) > grid.ClientSize.Width);
            }
            finally
            {
                AppLocalization.SetLanguage(AppLanguage.SimplifiedChinese);
            }
        });

    [Fact]
    public void MainPagesExposeHelpAndLanguageSelector()
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
                new BackupControl(services, _ => { })
            };

            try
            {
                foreach (var page in pages)
                {
                    _ = page.Handle;
                    Assert.Single(Descendants<Button>(page), button => button.Name == "PageHelpButton");
                }

                using var main = new MainForm(services);
                _ = main.Handle;
                var selector = Assert.Single(
                    Descendants<ComboBox>(main),
                    item => item.Name == "UiLanguageSelector");
                Assert.Equal(2, selector.Items.Count);
                Assert.Contains("     Git Services", Descendants<Button>(main).Select(button => button.Text));
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

    private static void BindGrid(DataGridView grid, IReadOnlyList<GridLayoutRow> rows)
    {
        grid.DataSource = null;
        grid.DataSource = rows.ToList();
        grid.PerformLayout();
        Application.DoEvents();
    }

    private static void AssertGridLayout(
        DataGridView grid,
        IReadOnlyDictionary<string, UiHelpers.GridColumnWidthRange>? logicalOverrides = null)
    {
        Assert.Equal(DataGridViewAutoSizeColumnsMode.None, grid.AutoSizeColumnsMode);
        Assert.Equal(ScrollBars.Both, grid.ScrollBars);
        var visibleColumns = grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .ToList();
        Assert.NotEmpty(visibleColumns);
        Assert.All(
            visibleColumns,
            column =>
            {
                var logicalRange = logicalOverrides is not null
                    && logicalOverrides.TryGetValue(column.Name, out var configuredRange)
                        ? configuredRange
                        : UiHelpers.DefaultGridColumnWidthRange;
                var scaledRange = UiHelpers.ScaleGridColumnWidthRange(logicalRange, grid.DeviceDpi);
                Assert.Equal(DataGridViewAutoSizeColumnMode.None, column.AutoSizeMode);
                Assert.Equal(scaledRange.MinimumWidth, column.MinimumWidth);
                Assert.InRange(column.Width, scaledRange.MinimumWidth, scaledRange.MaximumWidth);
            });
    }

    private static DataGridViewColumn GridColumn(DataGridView grid, string name)
        => Assert.Single(
            grid.Columns.Cast<DataGridViewColumn>(),
            column => string.Equals(column.Name, name, StringComparison.Ordinal));

    private static int VisibleColumnWidth(DataGridView grid)
        => grid.Columns.Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .Sum(column => column.Width);

    private static IReadOnlyDictionary<string, UiHelpers.GridColumnWidthRange> GitServiceGridColumnWidths()
        => new Dictionary<string, UiHelpers.GridColumnWidthRange>(StringComparer.Ordinal)
        {
            ["Web地址"] = new(220, 520)
        };

    private static AppConfig CreateGitServiceLayoutConfig(bool longText)
    {
        var suffix = longText ? new string('x', 1000) : "short";
        var service = new GitServiceInstance
        {
            Id = "layout-service",
            DisplayName = longText ? $"Enterprise source control service {suffix}" : "Service",
            ProviderKind = GitProviderKind.Generic,
            HostName = longText ? $"git-{suffix}.example.test" : "git.example.test",
            SshPort = 22,
            SshUser = longText ? $"git-{suffix}" : "git",
            WebBaseUrl = longText ? $"https://git.example.test/{suffix}" : "https://git.example.test",
            DefaultIdentityId = "layout-identity"
        };
        var identity = new GitIdentity
        {
            Id = service.DefaultIdentityId,
            ServiceInstanceId = service.Id,
            DisplayName = longText ? $"Default engineering identity {suffix}" : "Default identity",
            AccountName = "layout-user",
            HostAlias = "layout-host"
        };
        return new AppConfig
        {
            GitServices = [service],
            Identities = [identity],
            RepositoryRoutes = []
        };
    }

    private static ApplicationServices CreateGitServicesOnlyApplicationServices(TestAppConfigStore configStore)
    {
        var providers = GitProviderAdapterRegistry.CreateDefault();
        return new ApplicationServices
        {
            Paths = null!,
            FileSystem = null!,
            ConfigStore = configStore,
            ToolchainService = null!,
            RequiredToolInstallerService = null!,
            BackupService = null!,
            GitProviderAdapters = providers,
            GitServiceService = new GitServiceService(configStore, null!, null!, null!, providers),
            GitProfileService = null!,
            IdentityService = null!,
            OwnerRouteService = null!,
            SshKeyService = null!,
            SshKeyRenameService = null!,
            SshConfigService = null!,
            GitUrlRewriteService = null!,
            DiagnosticService = null!,
            Logger = null!
        };
    }

    private sealed class GridLayoutRow
    {
        public string Name { get; init; } = string.Empty;

        public string Details { get; init; } = string.Empty;
    }

    private sealed class TestAppConfigStore : IAppConfigStore
    {
        public TestAppConfigStore(AppConfig config)
        {
            Config = config;
        }

        public string ConfigPath => "memory://config.json";

        public AppConfig Config { get; set; }

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Config);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
        {
            Config = config;
            return Task.CompletedTask;
        }
    }

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
