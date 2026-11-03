using GitKeyRouter.App.Cli;
using GitKeyRouter.App.Coordination;
using GitKeyRouter.App.Presentation;

namespace GitKeyRouter.App.Forms;

public sealed partial class MainForm
{
    public Task<GuiCommandResponse> RunForwardedCommandAsync(GuiCommandRequest request)
    {
        var completion = new TaskCompletionSource<GuiCommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!GuiCommandPipe.IsAllowed(request))
            return Task.FromResult(GuiCommandPipe.Error([], 3, "invalid_request", "Unsupported forwarded command."));
        if (IsDisposed || Disposing || _closing || !IsHandleCreated)
            return Task.FromResult(GuiCommandPipe.Error(request.Arguments, 4, "gui_busy", "The GUI is starting or closing. Retry shortly."));
        try
        {
            BeginInvoke(new Action(async () =>
            {
                using var operation = GuiOperationGate.Shared.TryEnter();
                if (operation is null || _closing || IsDisposed || Disposing
                    || Application.OpenForms.Cast<Form>().Any(form => form != this && form.Modal))
                {
                    completion.TrySetResult(GuiCommandPipe.Error(request.Arguments, 4, "gui_busy",
                        "The GUI is editing or performing an operation. Finish or cancel that operation and retry; the GUI can stay open."));
                    return;
                }
                var wasEnabled = Enabled;
                Enabled = false;
                try
                {
                    using var input = new StringReader(request.StandardInput ?? string.Empty);
                    using var output = new StringWriter();
                    using var error = new StringWriter();
                    var exitCode = await new CliApplication(_services, input, output, error).RunAsync(request.Arguments);
                    try
                    {
                        if (_pages.TryGetValue(_activePageKey, out var page) && page is IAsyncRefreshable refreshable)
                            await UiHelpers.RefreshPreservingSelectionAsync(page, refreshable.RefreshAsync);
                        SetStatus(AppLocalization.T("命令行操作已完成，页面已刷新。", "CLI operation completed; view refreshed."));
                    }
                    catch (Exception exception)
                    {
                        // A refresh failure must not turn an already committed write into a retryable failure.
                        _services.Logger.Error("Failed to refresh after a forwarded CLI command.", exception);
                        SetStatus(AppLocalization.T("命令行操作已完成，请手动刷新页面。", "CLI operation completed; refresh the view manually."));
                    }
                    completion.TrySetResult(new GuiCommandResponse(exitCode, output.ToString(), error.ToString()));
                }
                catch (Exception exception)
                {
                    completion.TrySetResult(GuiCommandPipe.Error(request.Arguments, 2, "operation_failed", exception.Message));
                }
                finally
                {
                    if (!IsDisposed) Enabled = wasEnabled;
                }
            }));
        }
        catch (InvalidOperationException)
        {
            completion.TrySetResult(GuiCommandPipe.Error(request.Arguments, 4, "gui_busy", "The GUI is closing. Retry shortly."));
        }
        return completion.Task;
    }
}
