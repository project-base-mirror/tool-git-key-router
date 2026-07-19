using System.Runtime.ExceptionServices;
using GitKeyRouter.App;
using GitKeyRouter.App.Controls;
using GitKeyRouter.App.Forms;
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
