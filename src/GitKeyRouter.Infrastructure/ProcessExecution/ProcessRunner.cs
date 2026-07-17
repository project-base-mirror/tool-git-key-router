using System.Diagnostics;
using GitKeyRouter.Core.Abstractions;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Infrastructure.ProcessExecution;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var output = new List<string>();
        var errors = new List<string>();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request),
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                lock (output)
                {
                    output.Add(eventArgs.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                lock (errors)
                {
                    errors.Add(eventArgs.Data);
                }
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{request.ExecutablePath}'.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ProcessResult
            {
                ExecutablePath = request.ExecutablePath,
                Arguments = request.Arguments,
                Duration = stopwatch.Elapsed,
                StartException = exception,
                StandardError = exception.Message
            };
        }

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var timedOut = false;
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            TryKill(process);
        }

        stopwatch.Stop();
        return new ProcessResult
        {
            ExecutablePath = request.ExecutablePath,
            Arguments = request.Arguments,
            ExitCode = process.HasExited ? process.ExitCode : null,
            StandardOutput = JoinLines(output),
            StandardError = JoinLines(errors),
            TimedOut = timedOut,
            Cancelled = cancelled,
            Duration = stopwatch.Elapsed
        };
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = request.CreateNoWindow,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in request.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // The original timeout/cancellation result is more useful than a secondary kill error.
        }
    }

    private static string JoinLines(IEnumerable<string> lines)
        => string.Join(Environment.NewLine, lines);
}
