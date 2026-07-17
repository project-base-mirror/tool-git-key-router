using GitKeyRouter.App.Cli;
using GitKeyRouter.App.Forms;

namespace GitKeyRouter.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var services = AppBootstrapper.CreateServices();
        if (args.Length > 0)
        {
            ConsoleBridge.Attach();
            try
            {
                return new CliApplication(services).RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                services.Logger.Error("Unhandled CLI error.", exception);
                Console.Error.WriteLine(exception);
                return 3;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, eventArgs) =>
        {
            services.Logger.Error("Unhandled UI thread error.", eventArgs.Exception);
            MessageBox.Show(eventArgs.Exception.ToString(), "GitKeyRouter - 未处理错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.Run(new MainForm(services));
        return 0;
    }
}
