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

        // Preserve pipes/files supplied by an AI runner or shell before attaching a console.
        var outputHandle = GetRedirectedHandle(-11);
        var errorHandle = GetRedirectedHandle(-12);
        if (outputHandle != nint.Zero && errorHandle != nint.Zero)
        {
            ResetStreams(outputHandle, errorHandle);
            return;
        }

        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }

        ResetStreams(outputHandle, errorHandle);
    }

    private static nint GetRedirectedHandle(int standardHandle)
    {
        var handle = GetStdHandle(standardHandle);
        return handle != nint.Zero && handle != new nint(-1) && GetFileType(handle) is 1 or 3
            ? handle
            : nint.Zero;
    }

    private static void ResetStreams(nint outputHandle, nint errorHandle)
    {
        outputHandle = outputHandle != nint.Zero ? outputHandle : GetStdHandle(-11);
        errorHandle = errorHandle != nint.Zero ? errorHandle : GetStdHandle(-12);
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
    private static extern uint GetFileType(nint handle);

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int standardHandle);
}
