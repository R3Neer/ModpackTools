namespace ModpackTools.Core.Transactions;

// Mutex ownership is thread-affine. A small dedicated owner keeps it across all
// asynchronous preparation/IO and releases it on that SAME thread. Process death
// leaves an abandoned mutex, allowing the next owner to recover the durable journal.
internal sealed class NamedProjectLease : IAsyncDisposable
{
    private readonly ManualResetEventSlim release = new(false);
    private readonly TaskCompletionSource<NamedProjectLease> granted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposed;
    public bool WasAbandoned { get; private set; }

    public static Task<NamedProjectLease> AcquireAsync(string physicalIdentity)
    {
        var lease = new NamedProjectLease();
        var owner = new Thread(() => lease.Own("Global\\ModpackTools.Project.v1." + physicalIdentity)) { IsBackground = true, Name = "ModpackTools project lease" };
        try { owner.Start(); }
        catch { lease.release.Dispose(); throw; }
        return lease.granted.Task;
    }

    private void Own(string name)
    {
        try
        {
            using var mutex = new Mutex(false, name); var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; WasAbandoned = true; }
                if (!acquired) throw WindowsProjectIdentity.Error("Busy", "Another operation owns this project transaction");
                granted.TrySetResult(this); release.Wait();
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch (Exception error)
        {
            granted.TrySetException(error is DomainException ? error : WindowsProjectIdentity.Error("LeaseFailed", "Project ownership could not be acquired safely", details: error.Message));
        }
        finally { release.Dispose(); finished.TrySetResult(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) release.Set();
        await finished.Task.ConfigureAwait(false);
    }
}
