using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GitKeyRouter.App.Cli;

internal static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    public static void Attach()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }

        ResetStreams();
    }

    private static void ResetStreams()
    {
        var outputHandle = GetStdHandle(-11);
        var errorHandle = GetStdHandle(-12);
        if (outputHandle != nint.Zero && outputHandle != new nint(-1))
        {
            var output = new FileStream(new SafeFileHandle(outputHandle, false), FileAccess.Write);
            Console.SetOut(new StreamWriter(output) { AutoFlush = true });
        }

        if (errorHandle != nint.Zero && errorHandle != new nint(-1))
        {
            var error = new FileStream(new SafeFileHandle(errorHandle, false), FileAccess.Write);
            Console.SetError(new StreamWriter(error) { AutoFlush = true });
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int standardHandle);
}
