using System.Diagnostics;

namespace ScrollWM.Windows;

// Fixture windows intentionally do not take this lock; only managers do.
public sealed class SessionLock : IDisposable
{
    private readonly Mutex mutex;

    public SessionLock()
    {
        using var process = Process.GetCurrentProcess();
        mutex = new Mutex(false, $"Local\\ScrollWM.Session.{process.SessionId}");
        var acquired = false;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            throw new InvalidOperationException("Another ScrollWM manager or experiment is already running in this session.");
        }
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
