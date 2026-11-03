namespace GitKeyRouter.App.Coordination;

// The GUI owns the cross-process single-instance mutex for its lifetime. All GUI
// actions and forwarded CLI writes share this shorter async-safe operation gate.
public sealed class GuiOperationGate
{
    public static GuiOperationGate Shared { get; } = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsBusy => _gate.CurrentCount == 0;

    public IDisposable? TryEnter() => _gate.Wait(0) ? new Lease(_gate) : null;

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
