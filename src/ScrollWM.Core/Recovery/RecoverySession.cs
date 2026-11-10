using ScrollWM.Core.Windows;

namespace ScrollWM.Core.Recovery;

public sealed record RecoveryJournal(int Version, DateTimeOffset CreatedUtc, WindowSnapshot[] Windows)
{
    public const int CurrentVersion = 2;
}

public interface IRecoveryStore
{
    RecoveryJournal? Load();
    void Save(RecoveryJournal journal);
}

public interface IRestorableWindows
{
    WindowSnapshot Capture(WindowIdentity identity);
    IdentityStatus CheckIdentity(WindowSnapshot snapshot);
    void Restore(WindowSnapshot snapshot);
    void ReleaseToken(WindowSnapshot snapshot);
    void RestorePresentation(WindowSnapshot snapshot) { }
}

public sealed record RestoreResult(int Restored, int ClosedOrReused, int Remaining, string[] Errors);

// All calls are serialized by the host. A journal is written before the host
// receives snapshots that can be moved. Failed restores stay in the journal.
public sealed class RecoverySession(IRecoveryStore store, IRestorableWindows windows)
{
    public RecoveryJournal? Pending => store.Load();

    public WindowSnapshot[] Begin(IEnumerable<WindowIdentity> identities)
    {
        if (store.Load() is { Windows.Length: > 0 })
        {
            throw new InvalidOperationException("Restore the pending experiment before starting another.");
        }

        return CaptureAndSave(identities, null);
    }

    public WindowSnapshot[] Append(IEnumerable<WindowIdentity> identities)
    {
        var journal = store.Load() ?? throw new InvalidOperationException("Start a session before adding windows.");
        return CaptureAndSave(identities, journal);
    }

    private WindowSnapshot[] CaptureAndSave(IEnumerable<WindowIdentity> identities, RecoveryJournal? existing)
    {
        var selected = identities.ToArray();
        if (selected.Length == 0 || selected.Select(item => item.Handle).Distinct().Count() != selected.Length)
        {
            throw new ArgumentException("Select at least one distinct window.", nameof(identities));
        }

        if (selected.Any(item => existing?.Windows.Any(saved => saved.Identity.Handle == item.Handle) == true))
        {
            throw new ArgumentException("A selected window is already captured.", nameof(identities));
        }

        var snapshots = new List<WindowSnapshot>();
        try
        {
            foreach (var identity in selected)
            {
                snapshots.Add(windows.Capture(identity));
            }

            var result = snapshots.ToArray();
            store.Save(new(RecoveryJournal.CurrentVersion, existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
                [.. existing?.Windows ?? [], .. result]));
            return result;
        }
        catch
        {
            foreach (var snapshot in snapshots)
            {
                windows.ReleaseToken(snapshot);
            }

            throw;
        }
    }

    public RestoreResult RestoreAll()
    {
        var journal = store.Load();
        if (journal is null || journal.Windows.Length == 0)
        {
            return new(0, 0, 0, []);
        }

        var remaining = journal.Windows.ToList();
        var errors = new List<string>();
        var restored = 0;
        var gone = 0;

        foreach (var snapshot in journal.Windows)
        {
            try
            {
                var status = windows.CheckIdentity(snapshot);
                if (status == IdentityStatus.Unavailable)
                {
                    errors.Add($"Window {snapshot.Identity.Handle}: identity cannot be verified; retained for retry.");
                    continue;
                }

                if (status == IdentityStatus.Matches)
                {
                    windows.Restore(snapshot);
                }

                // Checkpoint before removing the token. If saving fails, the
                // original journal and marker still allow another restore.
                var next = remaining.Where(item => item != snapshot).ToArray();
                store.Save(journal with { Windows = next });
                remaining = next.ToList();
                if (status == IdentityStatus.Matches)
                {
                    restored++;
                    windows.ReleaseToken(snapshot);
                }
                else
                {
                    gone++;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                errors.Add($"Window {snapshot.Identity.Handle}: {exception.Message}");
            }
        }

        return new(restored, gone, remaining.Count, errors.ToArray());
    }

    // Used only when a captured window closes or the user deliberately detaches
    // it. Persist removal before releasing the marker; never reposition it.
    public bool Forget(WindowIdentity identity, bool restorePresentation = false)
    {
        var journal = store.Load();
        var snapshot = journal?.Windows.SingleOrDefault(item => item.Identity == identity);
        if (journal is null || snapshot is null) { return false; }
        if (restorePresentation) { windows.RestorePresentation(snapshot); }
        store.Save(journal with { Windows = journal.Windows.Where(item => item.Identity != identity).ToArray() });
        windows.ReleaseToken(snapshot);
        return true;
    }
}
