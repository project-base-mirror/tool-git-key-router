using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using GitKeyRouter.App.Cli;

namespace GitKeyRouter.App.Coordination;

public sealed record GuiCommandRequest(int ProtocolVersion, string[] Arguments, string? StandardInput);
public sealed record GuiCommandResponse(int ExitCode, string StandardOutput, string StandardError);

public static class GuiCommandPipe
{
    public const int ProtocolVersion = 1;
    private const int MaxMessageBytes = 4 * 1024 * 1024;

    public static bool IsAllowed(GuiCommandRequest request)
        => request.ProtocolVersion == ProtocolVersion
            && request.Arguments is { Length: > 0 and <= 32 }
            && request.Arguments.All(argument => argument is not null && argument.Length <= 32_768)
            && CliApplication.RequiresExclusiveInstance(request.Arguments)
            && CliApplication.ValidateArguments(request.Arguments) is null
            && (!request.Arguments.Contains("--file", StringComparer.OrdinalIgnoreCase)
                || FileArgument(request.Arguments) == "-");

    public static async Task<GuiCommandRequest> CreateRequestAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var arguments = args.ToArray();
        string? input = null;
        var fileIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "--file", StringComparison.OrdinalIgnoreCase));
        if (fileIndex >= 0)
        {
            var file = arguments[fileIndex + 1];
            if (file == "-")
            {
                input = await ReadInputAsync(Console.In, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var reader = File.OpenText(Path.GetFullPath(file));
                input = await ReadInputAsync(reader, cancellationToken).ConfigureAwait(false);
            }
            arguments[fileIndex + 1] = "-";
        }
        return new GuiCommandRequest(ProtocolVersion, arguments, input);
    }

    public static async Task<GuiCommandResponse> SendAsync(string pipeName, GuiCommandRequest request, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // Allow a short queue of callers while the single server finishes a prior write.
        await pipe.ConnectAsync(10_000, cancellationToken).ConfigureAwait(false);
        await WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        // Do not time out and then retry a write whose outcome may already be committed.
        return await ReadAsync<GuiCommandResponse>(pipe, cancellationToken).ConfigureAwait(false);
    }

    public static GuiCommandResponse Error(string[] args, int exitCode, string code, string message)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        CliApplication.ReportError(args, exitCode, code, message, output, error);
        return new GuiCommandResponse(exitCode, output.ToString(), error.ToString());
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes)
        {
            throw new InvalidDataException("Invalid command message size.");
        }
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty command message.");
    }

    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxMessageBytes)
        {
            throw new InvalidDataException("Command message exceeds 4 MiB.");
        }
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? FileArgument(string[] args)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, "--file", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task<string> ReadInputAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > MaxMessageBytes / 2)
            {
                throw new InvalidDataException("Forwarded JSON record exceeds 2 Mi characters.");
            }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}

public sealed class GuiCommandServer : IDisposable
{
    private readonly string _pipeName;
    private readonly Func<GuiCommandRequest, Task<GuiCommandResponse>> _execute;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _listener;
    private int _disposed;

    public GuiCommandServer(string pipeName, Func<GuiCommandRequest, Task<GuiCommandResponse>> execute)
    {
        _pipeName = pipeName;
        _execute = execute;
        _listener = ListenAsync(CreatePipe());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        // The callback may be marshalled to the GUI thread. Never join it from that thread.
        _ = _listener.ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
    }

    private NamedPipeServerStream CreatePipe()
        => new(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream? initialPipe)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = initialPipe ?? CreatePipe();
                initialPipe = null;
                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                GuiCommandRequest request;
                using (var receive = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
                {
                    receive.CancelAfter(TimeSpan.FromSeconds(5));
                    request = await GuiCommandPipe.ReadAsync<GuiCommandRequest>(pipe, receive.Token).ConfigureAwait(false);
                }
                var response = GuiCommandPipe.IsAllowed(request)
                    ? await _execute(request).WaitAsync(_shutdown.Token).ConfigureAwait(false)
                    : GuiCommandPipe.Error([], 3, "invalid_request", "Unsupported or unconfirmed forwarded command.");
                using var send = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                send.CancelAfter(TimeSpan.FromSeconds(5));
                await GuiCommandPipe.WriteAsync(pipe, response, send.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or JsonException)
            {
                // A disconnected or malformed caller does not stop the GUI or replay its write.
                if (!_shutdown.IsCancellationRequested)
                    await Task.Delay(50, _shutdown.Token).ConfigureAwait(false);
            }
        }
    }
}
