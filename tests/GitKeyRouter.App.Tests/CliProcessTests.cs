using System.Diagnostics;
using System.Text.Json;
using GitKeyRouter.App.Cli;

namespace GitKeyRouter.App.Tests;

public sealed class CliProcessTests
{
    [Fact]
    public void RedirectedCliWritesJsonToParentPipe()
    {
        var (exitCode, output, error) = RunChild("list-templates", "--json");
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("list-templates", document.RootElement.GetProperty("command").GetString());
        Assert.Contains(document.RootElement.GetProperty("data").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "Gitee");
    }

    [Fact]
    public void ConfirmedWriteHonorsGuiMutexAndReportsJsonBusyError()
    {
        // Hold and release the thread-affine mutex synchronously on the same thread.
        using var guard = SingleInstanceGuard.TryAcquire(signalExistingInstance: false);
        Assert.True(guard.IsPrimaryInstance);
        var (exitCode, output, error) = RunChild("delete-route", "test-only-missing", "--yes", "--json");
        Assert.Equal(4, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("instance_busy", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void InvalidWriteIsRejectedBeforeExclusiveStartup()
    {
        using var guard = SingleInstanceGuard.TryAcquire(signalExistingInstance: false);
        Assert.True(guard.IsPrimaryInstance);
        var (exitCode, output, error) = RunChild("apply", "--yes", "--typo", "--json");
        Assert.Equal(3, exitCode);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("invalid_arguments", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static (int ExitCode, string Output, string Error) RunChild(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.ChangeExtension(typeof(CliProcessTests).Assembly.Location, ".runtimeconfig.json"));
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("CLI did not exit.");
        }

        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
