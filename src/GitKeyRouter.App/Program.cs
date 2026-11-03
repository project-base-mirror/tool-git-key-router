using GitKeyRouter.App.Cli;
using GitKeyRouter.App.Coordination;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (CliApplication.CanRunWithoutServices(args))
        {
            ConsoleBridge.Attach();
            return CliApplication.RunWithoutServices(args);
        }

        if (args.Length > 0 && CliApplication.ValidateArguments(args) is { } argumentError)
        {
            ConsoleBridge.Attach();
            return CliApplication.ReportError(args, 3, "invalid_arguments", argumentError);
        }

        using var instanceGuard = CliApplication.RequiresExclusiveInstance(args)
            ? SingleInstanceGuard.TryAcquire(signalExistingInstance: args.Length == 0)
            : null;
        if (instanceGuard is { IsPrimaryInstance: false })
        {
            return args.Length == 0 ? 0 : ForwardToGui(args);
        }

        ApplicationServices? services = null;
        try
        {
            services = AppBootstrapper.CreateServices();
            if (instanceGuard is { IsPrimaryInstance: true })
            {
                var recovery = services.GitProfileService
                    .RecoverInterruptedTransactionsAsync()
                    .GetAwaiter()
                    .GetResult();
                if (!recovery.Success)
                {
                    throw new InvalidOperationException(
                        string.Join(Environment.NewLine, new[] { recovery.Message }.Concat(recovery.Errors)));
                }
            }

            if (args.Length > 0)
            {
                ConsoleBridge.Attach();
                return new CliApplication(services).RunAsync(args).GetAwaiter().GetResult();
            }

            var startupConfig = services.ConfigStore.LoadAsync().GetAwaiter().GetResult();
            AppLocalization.SetLanguage(AppLocalization.Parse(startupConfig.UiLanguage));
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, eventArgs) =>
            {
                services.Logger.Error("Unhandled UI thread error.", eventArgs.Exception);
                MessageBox.Show(eventArgs.Exception.ToString(), "GitKeyRouter - 未处理错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            using var mainForm = new MainForm(services);
            if (instanceGuard is { IsPrimaryInstance: true })
            {
                _ = mainForm.Handle;
                instanceGuard.StartListening(() =>
                {
                    if (mainForm.IsDisposed || mainForm.Disposing || !mainForm.IsHandleCreated)
                    {
                        return;
                    }

                    try
                    {
                        mainForm.BeginInvoke(new Action(mainForm.ActivateFromSecondaryInstance));
                    }
                    catch (ObjectDisposedException)
                    {
                        // The application is already closing.
                    }
                    catch (InvalidOperationException)
                    {
                        // The window handle can disappear while shutdown is in progress.
                    }
                });
            }

            using var commandServer = new GuiCommandServer(SingleInstanceGuard.CommandPipeName, mainForm.RunForwardedCommandAsync);
            Application.Run(mainForm);
            return 0;
        }
        catch (Exception exception)
        {
            services?.Logger.Error("Fatal application startup error.", exception);
            if (args.Length > 0)
            {
                ConsoleBridge.Attach();
                return CliApplication.ReportError(args, 2, "startup_failed", exception.Message);
            }
            else
            {
                MessageBox.Show(
                    exception.ToString(),
                    "GitKeyRouter - 启动失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 3;
        }
    }

    private static int ForwardToGui(string[] args)
    {
        ConsoleBridge.Attach();
        try
        {
            var request = GuiCommandPipe.CreateRequestAsync(args).GetAwaiter().GetResult();
            var response = GuiCommandPipe.SendAsync(SingleInstanceGuard.CommandPipeName, request).GetAwaiter().GetResult();
            Console.Out.Write(response.StandardOutput);
            Console.Error.Write(response.StandardError);
            return response.ExitCode;
        }
        catch (TimeoutException)
        {
            return CliApplication.ReportError(args, 4, "instance_busy",
                "The current instance is busy or does not support CLI forwarding. Retry after its operation finishes; upgrade an older GUI to use forwarding.");
        }
        catch (Exception exception)
        {
            return CliApplication.ReportError(args, 2, "forwarding_failed",
                $"Could not complete the forwarded command: {exception.Message}. If the connection was interrupted, inspect the current configuration before retrying.");
        }
    }
}
