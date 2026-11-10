using ScrollWM.Core.Management;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public partial class StripSessionTests
{
    [Fact]
    public async Task NewWindowIsJournaledBeforeMoveAndExistingWidthsSurvive()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3), Id(4)]);
        var widths = session.Strip!.Columns.ToDictionary(item => item.Identity, item => item.Width);
        await session.AddAsync([Id(5)]);
        Assert.All(session.Strip.Columns.Where(item => widths.ContainsKey(item.Identity)), item => Assert.Equal(widths[item.Identity], item.Width));
        Assert.Equal(5, desktop.Store.Journal!.Windows.Length);
        Assert.Equal(Id(5), session.Strip.Focused);
        await session.PauseAsync();
        Assert.Equal(5, session.LastRestore!.Restored);
        Assert.False(session.Running);
    }

    [Fact]
    public async Task NativeMinimumWidthFeedsBackIntoNeighborPositions()
    {
        var desktop = new Desktop { MinimumWidth = 900 };
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        Assert.All(session.Strip!.Columns, item => Assert.Equal(900, item.Width));
        var targets = session.Strip.Layout();
        Assert.Equal(targets[0].Bounds.Right + 12, targets[1].Bounds.Left);
        Assert.All(targets, item => Assert.Equal(item.Bounds, desktop.ReadFrame(item.Identity.Handle)));
    }

    [Fact]
    public async Task FailedPlacementStopsAndRestoresEveryCapturedWindow()
    {
        var desktop = new Desktop { FailedMove = 2 };
        var session = Session(desktop);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync([Id(1), Id(2)]));
        Assert.False(session.Running);
        Assert.Equal(2, session.LastRestore!.Restored);
        Assert.Empty(desktop.Store.Journal!.Windows);
    }

    [Fact]
    public async Task PendingRecoveryIsNeverMovedOrOverwrittenByStart()
    {
        var desktop = new Desktop();
        new RecoverySession(desktop.Store, desktop).Begin([Id(1)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Session(desktop).StartAsync([Id(2)]));
        Assert.Empty(desktop.Moves);
        Assert.Equal(Id(1), Assert.Single(desktop.Store.Journal!.Windows).Identity);
        Assert.Empty(desktop.Restored);
    }

    [Fact]
    public async Task PauseResumeRetainsOrderAndChosenWidths()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        await session.NavigateAsync(1);
        await session.CycleWidthAsync();
        await session.ReorderAsync(-1);
        var columns = session.Strip!.Columns.ToArray();
        await session.PauseAsync();
        await session.ResumeAsync();
        Assert.Equal(columns, session.Strip.Columns);
        Assert.True(session.Running);
    }

    [Fact]
    public async Task ClosedAndMinimizedWindowsLeaveStripAndExternalFocusIsRevealed()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3)]);
        desktop.Closed.Add(2);
        desktop.Excluded.Add(1);
        desktop.FocusedHandle = 3;
        await session.ReconcileAsync();
        Assert.Equal(Id(3), Assert.Single(session.Strip!.Columns).Identity);
        Assert.Equal(0, session.Strip.Offset);
        await session.PauseAsync();
        Assert.Equal(1, session.LastRestore!.ClosedOrReused);
        Assert.Equal(2, session.LastRestore.Restored);
    }

    [Fact]
    public async Task DisplayChangePausesAndRestoresInsteadOfMovingIntoAnotherScreen()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        desktop.MultipleDisplays = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReconcileAsync());
        Assert.False(session.Running);
        Assert.Equal(2, session.LastRestore!.Restored);
    }

    [Fact]
    public async Task JournalFailureNeverPermitsWindowMovement()
    {
        var desktop = new Desktop();
        desktop.Store.FailSave = true;
        await Assert.ThrowsAsync<IOException>(() => Session(desktop).StartAsync([Id(1)]));
        Assert.Empty(desktop.Moves);
    }

    private static StripSession Session(Desktop desktop) => new(desktop, desktop.Store, () => Task.CompletedTask);

    [Fact]
    public async Task StartupPlacesForegroundFirstAndPreservesRemainingOrder()
    {
        var desktop = new Desktop { FocusedHandle = 3 };
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3), Id(4)]);
        Assert.Equal(new long[] { 3, 1, 2, 4 }, session.Strip!.Columns.Select(item => item.Identity.Handle));
        Assert.Equal(Id(3), session.Strip.Focused);
    }

    [Fact]
    public async Task ScaledDisplayUsesPhysicalMarginsAndGaps()
    {
        var desktop = new Desktop { Dpi = 144 };
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        Assert.Equal(new PixelRect(9, 9, 1182, 882), session.Strip!.Viewport);
        Assert.Equal(18, session.Strip.Gap);
        Assert.All(session.Strip.Layout(), target => Assert.Equal(target.Bounds, desktop.Moves[target.Identity.Handle]));
    }
    private static WindowIdentity Id(long handle) => new(handle, 10, 123, 20, "Fixture");

    private sealed class MemoryStore : IRecoveryStore
    {
        public RecoveryJournal? Journal { get; private set; }
        public bool FailSave { get; set; }
        public RecoveryJournal? Load() => Journal;
        public void Save(RecoveryJournal journal)
        {
            if (FailSave) { throw new IOException("Journal unavailable."); }
            Journal = journal;
        }
    }

    private sealed class Desktop : IStripDesktop
    {
        public MemoryStore Store { get; } = new();
        public Dictionary<long, PixelRect> Moves { get; } = [];
        public bool ParkHidden { get; set; }
        private readonly Dictionary<long, PixelRect> logicalFrames = [];
        public HashSet<long> Closed { get; } = [];
        public HashSet<long> Excluded { get; } = [];
        public List<long> Restored { get; } = [];
        public List<long> Released { get; } = [];
        public HashSet<WindowIdentity> Known { get; } = [Id(1), Id(2), Id(3), Id(4)];
        public HashSet<WindowIdentity> Eligible { get; } = [Id(1), Id(2), Id(3), Id(4)];
        public HashSet<long> CaptureUnavailable { get; } = [];
        public HashSet<long> UserMoving { get; } = [];
        public int MinimumWidth { get; set; } = 1;
        public long FailedMove { get; set; }
        public bool MultipleDisplays { get; set; }
        public uint Dpi { get; set; } = 96;
        public long FocusedHandle { get; set; } = 1;
        public DisplayInfo[]? Configuration { get; set; }
        public Dictionary<long, string> WindowDisplays { get; } = [];
        public List<(long Handle, string Display)> MoveCalls { get; } = [];
        public List<(long[] Edges, long[] Visible)> EdgeOrders { get; } = [];
        public void PlaceBehind(IReadOnlyList<WindowSnapshot> edges, IReadOnlyList<WindowSnapshot> visible) =>
            EdgeOrders.Add((edges.Select(item => item.Identity.Handle).ToArray(), visible.Select(item => item.Identity.Handle).ToArray()));
        public IReadOnlyList<DisplayInfo> GetDisplayConfiguration() => MultipleDisplays
            ? throw new InvalidOperationException("Display configuration changed.")
            : Configuration ?? [new("default", new(0, 0, 1200, 900), new(0, 0, 1200, 900), Dpi, true)];
        public string GetWindowDisplay(WindowIdentity identity) => WindowDisplays.GetValueOrDefault(identity.Handle, GetDisplayConfiguration()[0].Id);
        public void MoveOnDisplay(WindowSnapshot snapshot, PixelRect target, DisplayInfo display, PixelRect viewport)
        {
            MoveCalls.Add((snapshot.Identity.Handle, display.Id));
            logicalFrames[snapshot.Identity.Handle] = target;
            Move(snapshot, ParkHidden ? MonitorPlacement.PhysicalTarget(target, viewport, GetDisplayConfiguration()) : target);
            WindowDisplays[snapshot.Identity.Handle] = display.Id;
        }
        public PixelRect GetSingleDisplayWorkArea() => MultipleDisplays
            ? throw new InvalidOperationException("Multiple displays.") : new(0, 0, 1200, 900);
        public bool IsEligible(WindowSnapshot snapshot) => !Excluded.Contains(snapshot.Identity.Handle);
        public bool IsUserMoving(WindowSnapshot snapshot) => UserMoving.Contains(snapshot.Identity.Handle);
        public IReadOnlyList<WindowIdentity> EnumerateKnownWindows() => Known.ToArray();
        public IReadOnlyList<WindowIdentity> EnumerateEligibleWindows() => Eligible.Where(item => !Closed.Contains(item.Handle) && !Excluded.Contains(item.Handle)).ToArray();
        public uint ReadDpi(WindowSnapshot snapshot) => Closed.Contains(snapshot.Identity.Handle) ? 0 : Dpi;
        public WindowSnapshot Capture(WindowIdentity identity)
        {
            if (CaptureUnavailable.Contains(identity.Handle)) { throw new WindowUnavailableException("Window disappeared."); }
            var frame = Moves.GetValueOrDefault(identity.Handle, new(10, 20, 500, 400));
            return new(identity, new(0, 1, new(-1, -1), new(-1, -1), frame), frame, 96, identity.Handle + 100);
        }
        public IdentityStatus CheckIdentity(WindowSnapshot snapshot) => Closed.Contains(snapshot.Identity.Handle)
            ? IdentityStatus.ClosedOrReused : IdentityStatus.Matches;
        public void Move(WindowSnapshot snapshot, PixelRect target)
        {
            // Every changed window must already be recoverable, including additions.
            Assert.Contains(Store.Journal!.Windows, saved => saved.Identity == snapshot.Identity);
            if (snapshot.Identity.Handle == FailedMove) { throw new InvalidOperationException("Move failed."); }
            Moves[snapshot.Identity.Handle] = target with { Width = Math.Max(target.Width, MinimumWidth) };
        }
        public PixelRect ReadFrame(long handle) => Moves[handle];
        public PixelRect ReadManagedFrame(WindowSnapshot snapshot) => ParkHidden
            ? logicalFrames[snapshot.Identity.Handle] with { Width = Moves[snapshot.Identity.Handle].Width, Height = Moves[snapshot.Identity.Handle].Height }
            : ReadFrame(snapshot.Identity.Handle);
        public bool Focus(WindowSnapshot snapshot) { FocusedHandle = snapshot.Identity.Handle; return true; }
        public void Restore(WindowSnapshot snapshot) { Restored.Add(snapshot.Identity.Handle); }
        public void ReleaseToken(WindowSnapshot snapshot) { Released.Add(snapshot.Identity.Handle); }
    }

    [Fact]
    public async Task AutoEnrollmentCapturesOnlyNewWindowsAndKeepsOtherWidths()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        var width = session.Strip!.Columns[0].Width;
        desktop.Eligible.Add(Id(5));
        desktop.FocusedHandle = 5;
        await session.ReconcileAsync([new(5, DesktopWindowEventKind.Shown)]);
        Assert.Equal(new long[] { 1, 5 }, session.Strip.Columns.Select(item => item.Identity.Handle));
        Assert.Equal(width, session.Strip.Columns[0].Width);
        Assert.Equal(Id(5), session.Strip.Focused);
        Assert.Equal(2, desktop.Store.Journal!.Windows.Length);
    }

    [Fact]
    public async Task AutoEnrollmentIsOffByDefaultAndBackgroundAddDoesNotStealFocus()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)]);
        desktop.Eligible.Add(Id(5));
        await session.ReconcileAsync();
        Assert.Single(session.Strip!.Columns);
        await session.PauseAsync();
        desktop.Eligible.Remove(Id(5));
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        desktop.Eligible.Add(Id(5));
        await session.ReconcileAsync();
        Assert.Equal(Id(1), session.Strip.Focused);
        Assert.Equal(0, session.Strip.Offset);
    }

    [Fact]
    public async Task MinimizeRestoreKeepsChosenWidthAndDoesNotCaptureAgain()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        await session.CycleWidthAsync();
        var column = session.Strip!.Columns[0];
        desktop.Excluded.Add(1);
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.Minimized)]);
        Assert.DoesNotContain(session.Strip.Columns, item => item.Identity == Id(1));
        desktop.Excluded.Remove(1);
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.Restored)]);
        Assert.Equal(column, session.Strip.Columns.Single(item => item.Identity == Id(1)));
        Assert.Equal(2, desktop.Store.Journal!.Windows.Length);
    }

    [Fact]
    public async Task ManualResizeUpdatesWidthWithoutShrinkingNeighbors()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        var neighborWidth = session.Strip!.Columns[1].Width;
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        desktop.Moves[1] = desktop.Moves[1] with { Width = 760 };
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.MoveSizeEnded)]);
        Assert.Equal(760, session.Strip.Columns[0].Width);
        Assert.Null(session.Strip.Columns[0].Preset);
        Assert.Equal(neighborWidth, session.Strip.Columns[1].Width);
    }

    [Fact]
    public async Task DragDetachesWithoutMovingWindowBackOrReenrollingIt()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        desktop.Eligible.Add(Id(5));
        desktop.FocusedHandle = 5;
        await session.ReconcileAsync();
        var start = new DesktopWindowEvent(5, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        var userFrame = desktop.Moves[5] with { Left = 77, Top = 99 };
        desktop.Moves[5] = userFrame;
        await session.ReconcileAsync([new(5, DesktopWindowEventKind.MoveSizeEnded)]);
        await session.ReconcileAsync();
        Assert.Single(session.Strip!.Columns);
        Assert.DoesNotContain(desktop.Store.Journal!.Windows, item => item.Identity == Id(5));
        Assert.Equal(userFrame, desktop.Moves[5]);
        await session.PauseAsync();
        Assert.DoesNotContain(5L, desktop.Restored);
        Assert.Equal(userFrame, desktop.Moves[5]);
    }

    [Fact]
    public async Task ActiveGesturePreventsCommandsFromSnappingWindowBack()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        desktop.Moves[1] = desktop.Moves[1] with { Left = 30 };
        await session.NavigateAsync(1);
        Assert.Equal(30, desktop.Moves[1].Left);
    }

    [Fact]
    public async Task VanishedNewWindowDoesNotStopTheRunningStrip()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        desktop.Eligible.Add(Id(5));
        desktop.CaptureUnavailable.Add(5);
        await session.ReconcileAsync();
        Assert.True(session.Running);
        Assert.Single(session.Strip!.Columns);
    }

    [Fact]
    public async Task FailedAutoEnrollmentJournalNeverMovesNewWindow()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        desktop.Eligible.Add(Id(5));
        desktop.Store.FailSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReconcileAsync());
        Assert.False(session.Running);
        Assert.False(desktop.Moves.ContainsKey(5));
        Assert.Single(desktop.Store.Journal!.Windows);
    }

    [Fact]
    public async Task ClosedHandleCanBeReusedAfterItsOldJournalEntryIsRetired()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        desktop.Closed.Add(2);
        await session.ReconcileAsync();
        Assert.DoesNotContain(desktop.Store.Journal!.Windows, item => item.Identity == Id(2));
        desktop.Closed.Remove(2);
        var reused = Id(2) with { ProcessStartUtcTicks = 999 };
        await session.AddAsync([reused]);
        Assert.Contains(session.Strip!.Columns, item => item.Identity == reused);
    }

    [Fact]
    public async Task FallbackFinishesMissedGestureEndButDoesNotInterruptActiveInput()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        desktop.Moves[1] = desktop.Moves[1] with { Left = 35 };
        desktop.UserMoving.Add(1);
        await session.ReconcileAsync();
        Assert.Equal(2, session.Strip!.Columns.Count);
        desktop.UserMoving.Remove(1);
        await session.ReconcileAsync();
        Assert.DoesNotContain(session.Strip.Columns, item => item.Identity == Id(1));
        Assert.Equal(35, desktop.Moves[1].Left);
    }

    [Fact]
    public async Task DraggedWindowCanBeExplicitlyReaddedWithNewOriginalPlacement()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2)]);
        var start = new DesktopWindowEvent(2, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        var manual = desktop.Moves[2] with { Left = 80 };
        desktop.Moves[2] = manual;
        await session.ReconcileAsync([new(2, DesktopWindowEventKind.MoveSizeEnded)]);
        await session.AddAsync([Id(2)]);
        Assert.Equal(manual, desktop.Store.Journal!.Windows.Single(item => item.Identity == Id(2)).VisibleFrame);
    }

    [Fact]
    public async Task MinimizedColumnsReturnInOriginalOrderEvenWhenLaterColumnRestoresFirst()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3), Id(4)]);
        desktop.Excluded.UnionWith([1, 2]);
        await session.ReconcileAsync();
        desktop.Excluded.Remove(2);
        await session.ReconcileAsync();
        Assert.Equal(new long[] { 2, 3, 4 }, session.Strip!.Columns.Select(item => item.Identity.Handle));
        desktop.Excluded.Remove(1);
        await session.ReconcileAsync();
        Assert.Equal(new long[] { 1, 2, 3, 4 }, session.Strip.Columns.Select(item => item.Identity.Handle));
    }

    [Fact]
    public async Task CommandDeferredDuringCancelledDragAppliesAfterInputEnds()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3)]);
        var before = desktop.Moves.ToDictionary(item => item.Key, item => item.Value);
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        await session.NavigateAsync(1);
        Assert.All(before, item => Assert.Equal(item.Value, desktop.Moves[item.Key]));
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.MoveSizeEnded)]);
        Assert.Equal(2, desktop.FocusedHandle);
        Assert.Equal(Id(2), session.Strip!.Focused);
        Assert.All(session.Strip.Layout(), item => Assert.Equal(item.Bounds, desktop.Moves[item.Identity.Handle]));
    }

    [Fact]
    public async Task ResumeLeavesWindowsOpenedWhilePausedUnselected()
    {
        var desktop = new Desktop();
        var session = Session(desktop);
        await session.StartAsync([Id(1)], automaticEnrollment: true);
        await session.PauseAsync();
        desktop.Known.Add(Id(5));
        desktop.Eligible.Add(Id(5));
        await session.ResumeAsync();
        await session.ReconcileAsync();
        Assert.Equal(Id(1), Assert.Single(session.Strip!.Columns).Identity);
        desktop.Eligible.Add(Id(6));
        await session.ReconcileAsync();
        Assert.Contains(session.Strip.Columns, item => item.Identity == Id(6));
        Assert.DoesNotContain(session.Strip.Columns, item => item.Identity == Id(5));
    }
}
