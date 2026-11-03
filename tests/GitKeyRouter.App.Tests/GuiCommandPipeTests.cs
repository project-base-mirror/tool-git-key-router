using System.IO.Pipes;
using GitKeyRouter.App.Cli;
using GitKeyRouter.App.Coordination;

namespace GitKeyRouter.App.Tests;

public sealed class GuiCommandPipeTests
{
    [Fact]
    public void ForwardingRejectsUnconfirmedForeignAndFileReadingRequests()
    {
        Assert.False(GuiCommandPipe.IsAllowed(new(2, ["apply", "--yes"], null)));
        Assert.False(GuiCommandPipe.IsAllowed(new(1, ["apply"], null)));
        Assert.False(GuiCommandPipe.IsAllowed(new(1, ["gh", "--", "anything"], null)));
        Assert.False(GuiCommandPipe.IsAllowed(new(1, ["save-service", "--file", "other.json", "--yes"], null)));
        Assert.True(GuiCommandPipe.IsAllowed(new(1, ["save-service", "--file", "-", "--yes"], "{}")));
    }

    [Fact]
    public async Task PipeRecoversFromMalformedClientAndKeepsStreamsIsolated()
    {
        using var fixture = new CliManagementTests.CliFixture();
        var name = $"GitKeyRouter.Tests.{Guid.NewGuid():N}";
        using var server = new GuiCommandServer(name, async request =>
        {
            using var input = new StringReader(request.StandardInput ?? string.Empty);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await new CliApplication(fixture.Services, input, output, error).RunAsync(request.Arguments);
            return new(code, output.ToString(), error.ToString());
        });
        using (var broken = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await broken.ConnectAsync(2000);
            await broken.WriteAsync(new byte[] { 255, 255, 255, 127 });
            Assert.Equal(0, await broken.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        var request = new GuiCommandRequest(1, ["save-profile", "--file", "-", "--yes", "--json"],
            """{"Id":"mock","DisplayName":"Mock","UserName":"Test","UserEmail":"test@example.test"}""");
        var result = await GuiCommandPipe.SendAsync(name, request).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("\"command\": \"save-profile\"", result.StandardOutput);
        Assert.Single((await fixture.Store.LoadAsync()).GitProfiles);
    }

    [Fact]
    public async Task CancelledCommandUsesOnlyItsInjectedOutput()
    {
        using var fixture = new CliManagementTests.CliFixture();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var code = await new CliApplication(fixture.Services, new StringReader(""), output, error)
            .RunAsync(["delete-route", "missing", "--yes", "--json"], cancellation.Token);
        Assert.Equal(2, code);
        Assert.Contains("\"code\": \"cancelled\"", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void OperationLeaseCannotBeReleasedTwiceOrOwnedConcurrently()
    {
        var gate = new GuiOperationGate();
        var first = gate.TryEnter();
        Assert.NotNull(first);
        Assert.Null(gate.TryEnter());
        first.Dispose();
        using var next = gate.TryEnter();
        Assert.NotNull(next);
        first.Dispose();
        Assert.Null(gate.TryEnter());
    }
}
