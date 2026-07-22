using GitKeyRouter.App.Cli;
using GitKeyRouter.App.Forms;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationServices? services = null;
        try
        {
            services = AppBootstrapper.CreateServices();
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
            Application.Run(new MainForm(services));
            return 0;
        }
        catch (Exception exception)
        {
            services?.Logger.Error("Fatal application startup error.", exception);
            if (args.Length > 0)
            {
                ConsoleBridge.Attach();
                Console.Error.WriteLine(exception);
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
}
