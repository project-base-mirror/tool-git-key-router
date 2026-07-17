using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GitKeyRouter.App.Cli;

internal static partial class ConsoleBridge
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
        var output = new FileStream(new SafeFileHandle(GetStdHandle(-11), false), FileAccess.Write);
        var error = new FileStream(new SafeFileHandle(GetStdHandle(-12), false), FileAccess.Write);
        Console.SetOut(new StreamWriter(output) { AutoFlush = true });
        Console.SetError(new StreamWriter(error) { AutoFlush = true });
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);
}
