namespace Archivio.ViewModels;

/// <summary>
/// 非同期UI操作のキャンセル、世代、Busyスコープを一元管理する軽量Coordinator。
/// </summary>
public sealed class OperationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCts;
    private long _generation;
    private int _busyCount;
    private bool _disposed;

    public event Action<bool>? BusyChanged;

    public OperationHandle Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = new CancellationTokenSource();
            return new OperationHandle(this, ++_generation, _currentCts.Token);
        }
    }

    public bool IsCurrent(long generation)
    {
        lock (_gate) return !_disposed && generation == _generation;
    }

    public void Cancel()
    {
        lock (_gate) _currentCts?.Cancel();
    }

    public async Task ObserveAsync(Task operation, Action<Exception>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onFailure?.Invoke(ex); }
    }

    public BusyScope EnterBusy()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _busyCount++;
            if (_busyCount == 1) BusyChanged?.Invoke(true);
            return new BusyScope(this);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    private void ExitBusy()
    {
        lock (_gate)
        {
            if (_busyCount == 0) return;
            _busyCount--;
            if (_busyCount == 0) BusyChanged?.Invoke(false);
        }
    }

    public readonly struct OperationHandle
    {
        private readonly OperationCoordinator _owner;
        internal OperationHandle(OperationCoordinator owner, long generation, CancellationToken token)
        {
            _owner = owner;
            Generation = generation;
            Token = token;
        }

        public long Generation { get; }
        public CancellationToken Token { get; }
        public bool IsCurrent => _owner.IsCurrent(Generation);
    }

    public sealed class BusyScope : IDisposable
    {
        private OperationCoordinator? _owner;
        internal BusyScope(OperationCoordinator owner) => _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitBusy();
    }
}
