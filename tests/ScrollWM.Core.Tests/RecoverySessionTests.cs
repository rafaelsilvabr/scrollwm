using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public class RecoverySessionTests
{
    [Fact]
    public void JournalFailurePreventsBeginAndReleasesCapturedMarkers()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore { FailSave = true };
        var session = new RecoverySession(store, windows);

        Assert.Throws<IOException>(() => session.Begin([Identity(1), Identity(2)]));
        Assert.Equal(new long[] { 1, 2 }, windows.Released);
        Assert.Null(store.Journal);
    }

    [Fact]
    public void ExistingRecoveryCannotBeOverwrittenByANewSession()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1)]);

        Assert.Throws<InvalidOperationException>(() => session.Begin([Identity(2)]));
        Assert.Single(store.Journal!.Windows);
        Assert.Equal(1, store.Journal.Windows[0].Identity.Handle);
    }

    [Fact]
    public void RestoreCheckpointsSuccessAndRetainsFailuresForAnotherProcess()
    {
        var windows = new FakeWindows { FailedHandle = 2 };
        var store = new MemoryStore();
        new RecoverySession(store, windows).Begin([Identity(1), Identity(2), Identity(3)]);

        var result = new RecoverySession(store, windows).RestoreAll();

        Assert.Equal(2, result.Restored);
        Assert.Equal(1, result.Remaining);
        Assert.Single(result.Errors);
        Assert.Equal(2, Assert.Single(store.Journal!.Windows).Identity.Handle);
        Assert.Equal(new long[] { 1, 3 }, windows.Released);

        windows.FailedHandle = 0;
        var retry = new RecoverySession(store, windows).RestoreAll();
        Assert.Equal(1, retry.Restored);
        Assert.Equal(0, retry.Remaining);
    }

    [Fact]
    public void ReusedHandleIsNeverRestoredAndUnavailableIdentityRemainsPending()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1), Identity(2)]);
        windows.Statuses[1] = IdentityStatus.ClosedOrReused;
        windows.Statuses[2] = IdentityStatus.Unavailable;

        var result = session.RestoreAll();

        Assert.Equal(1, result.ClosedOrReused);
        Assert.Equal(1, result.Remaining);
        Assert.Empty(windows.Restored);
        Assert.Empty(windows.Released);
        Assert.Equal(2, Assert.Single(store.Journal!.Windows).Identity.Handle);
    }

    [Fact]
    public void FailedCheckpointLeavesMarkerAndJournalForRetry()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1)]);
        store.FailSave = true;

        var result = session.RestoreAll();

        Assert.Equal(1, result.Remaining);
        Assert.Single(store.Journal!.Windows);
        Assert.Empty(windows.Released);
        Assert.Single(windows.Restored);
    }

    [Fact]
    public void PartialCaptureFailureReleasesEarlierMarkersWithoutSaving()
    {
        var windows = new FakeWindows { FailedCapture = 2 };
        var store = new MemoryStore();

        Assert.Throws<InvalidOperationException>(() =>
            new RecoverySession(store, windows).Begin([Identity(1), Identity(2)]));

        Assert.Null(store.Journal);
        Assert.Equal(new long[] { 1 }, windows.Released);
    }

    private static WindowIdentity Identity(long handle) => new(handle, 10, 123, 20, "Fixture");

    [Fact]
    public void FailedAppendPreservesEarlierJournalAndOnlyReleasesNewMarkers()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1)]);
        store.FailSave = true;
        Assert.Throws<IOException>(() => session.Append([Identity(2)]));
        Assert.Equal(Identity(1), Assert.Single(store.Journal!.Windows).Identity);
        Assert.Equal(new long[] { 2 }, windows.Released);
    }

    [Fact]
    public void ForgetCheckpointsBeforeReleasingMarkerAndNeverRestoresTheWindow()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1), Identity(2)]);
        store.FailSave = true;
        Assert.Throws<IOException>(() => session.Forget(Identity(2)));
        Assert.Empty(windows.Released);
        Assert.Equal(2, store.Journal!.Windows.Length);
        store.FailSave = false;
        Assert.True(session.Forget(Identity(2)));
        Assert.Single(store.Journal.Windows);
        Assert.Equal(new long[] { 2 }, windows.Released);
        Assert.Empty(windows.Restored);
    }

    [Fact]
    public void DetachRestoresPresentationBeforeCheckpointAndRetainsRecoveryOnSaveFailure()
    {
        var windows = new FakeWindows();
        var store = new MemoryStore();
        var session = new RecoverySession(store, windows);
        session.Begin([Identity(1)]);
        store.FailSave = true;
        Assert.Throws<IOException>(() => session.Forget(Identity(1), restorePresentation: true));
        Assert.Equal(new long[] { 1 }, windows.PresentationRestored);
        Assert.Single(store.Journal!.Windows);
        Assert.Empty(windows.Released);
        Assert.Empty(windows.Restored);
    }

    private sealed class MemoryStore : IRecoveryStore
    {
        public RecoveryJournal? Journal { get; private set; }
        public bool FailSave { get; set; }
        public RecoveryJournal? Load() => Journal;
        public void Save(RecoveryJournal journal)
        {
            if (FailSave)
            {
                throw new IOException("Journal write failed.");
            }

            Journal = journal;
        }
    }

    private sealed class FakeWindows : IRestorableWindows
    {
        public long FailedHandle { get; set; }
        public long FailedCapture { get; set; }
        public Dictionary<long, IdentityStatus> Statuses { get; } = [];
        public List<long> Restored { get; } = [];
        public List<long> Released { get; } = [];
        public List<long> PresentationRestored { get; } = [];
        public void RestorePresentation(WindowSnapshot snapshot) => PresentationRestored.Add(snapshot.Identity.Handle);

        public WindowSnapshot Capture(WindowIdentity identity)
        {
            if (identity.Handle == FailedCapture)
            {
                throw new InvalidOperationException("Window disappeared during capture.");
            }

            return new(identity, new(0, 1, new(-1, -1), new(-1, -1), new(30, 40, 500, 400)),
                new(30, 40, 500, 400), 96, identity.Handle + 100);
        }

        public IdentityStatus CheckIdentity(WindowSnapshot snapshot) =>
            Statuses.GetValueOrDefault(snapshot.Identity.Handle, IdentityStatus.Matches);

        public void Restore(WindowSnapshot snapshot)
        {
            if (snapshot.Identity.Handle == FailedHandle)
            {
                throw new InvalidOperationException("Application refused restoration.");
            }

            Restored.Add(snapshot.Identity.Handle);
        }

        public void ReleaseToken(WindowSnapshot snapshot) => Released.Add(snapshot.Identity.Handle);
    }
}
