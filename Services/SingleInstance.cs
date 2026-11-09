namespace QuickOrbit.Services;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\QuickOrbit.SingleInstance.7D21F8A4";
    private const string SignalName = @"Local\QuickOrbit.ShowSignal.7D21F8A4";

    private readonly Mutex? _mutex;
    private EventWaitHandle? _signal;
    private RegisteredWaitHandle? _registeredWait;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (createdNew)
        {
            _ownsMutex = true;
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _signal,
                (_, _) => SecondInstance?.Invoke(),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        else
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(SignalName);
                existing.Set();
            }
            catch
            {
                // The primary instance may still be initializing. Starting a second copy is still blocked.
            }
        }
    }

    public bool IsFirstInstance { get; }

    public event Action? SecondInstance;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(null);
        _signal?.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // The mutex may have been abandoned during shutdown.
            }
        }

        _mutex?.Dispose();
    }
}
