namespace Sati.Services;

/// <summary>
/// Holds the workstation-wide desktop launch slot for the lifetime of the WPF dispatcher.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string DesktopMutexName = @"Global\SatiLogica.Sati.Desktop.SingleInstance";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    internal static SingleInstanceGuard? TryAcquire() => TryAcquire(DesktopMutexName);

    internal static SingleInstanceGuard? TryAcquire(string name)
    {
        Mutex mutex;
        try { mutex = new Mutex(initiallyOwned: false, name); }
        catch (UnauthorizedAccessException)
        {
            // Another Windows session may own the global mutex with a DACL that
            // prevents this account from opening it. Block this launch as well.
            return null;
        }

        bool acquired;
        try
        {
            // A zero-timeout wait is atomic across processes. An abandoned owner
            // means its process ended without a clean exit, so this launch may run.
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }

            if (acquired)
                return new SingleInstanceGuard(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        // WPF calls OnStartup and OnExit on the same dispatcher thread. Windows
        // mutex ownership is thread-affine, so release it there before closing.
        var mutex = _mutex;
        if (mutex is null)
            return;

        _mutex = null;
        try { mutex.ReleaseMutex(); }
        finally { mutex.Dispose(); }
    }
}
