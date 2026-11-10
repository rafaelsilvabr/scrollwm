using ScrollWM.Core.Layout;
using ScrollWM.Core.Management;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public partial class StripSessionTests
{
    [Fact]
    public async Task LongStripParksHiddenColumnsAndExternalFocusRevealsThemOnTheirOwnMonitor()
    {
        var desktop = TwoDisplays();
        desktop.ParkHidden = true;
        desktop.WindowDisplays[5] = "left";
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(5), Id(3)]);
        Assert.True(session.Running);
        Assert.All(desktop.Configuration!, display => Assert.False(MonitorPlacement.Intersects(desktop.Moves[5], display.Bounds)));
        var otherFrame = desktop.Moves[3];
        await session.CycleWidthAsync();
        Assert.True(MonitorPlacement.Contains(session.Strips["left"].Viewport, desktop.Moves[2]));
        Assert.Contains(desktop.EdgeOrders, call => call.Edges.Contains(2) && call.Visible.Contains(1));
        var widths = session.Strips["left"].Columns.Select(item => item.Width).ToArray();
        desktop.FocusedHandle = 2;
        await session.ReconcileAsync();
        Assert.True(MonitorPlacement.Contains(session.Strip!.Viewport, desktop.Moves[2]));
        desktop.FocusedHandle = 5;
        await session.ReconcileAsync();
        Assert.Equal("left", session.ActiveDisplayId);
        Assert.Equal(Id(5), session.Strip!.Focused);
        Assert.True(MonitorPlacement.Contains(session.Strip.Viewport, desktop.Moves[5]));
        Assert.Equal(otherFrame, desktop.Moves[3]);
        Assert.Equal(widths, session.Strip.Columns.Select(item => item.Width));
        await session.PauseAsync();
        Assert.Equal(4, session.LastRestore!.Restored);
        Assert.Empty(desktop.Store.Journal!.Windows);
    }

    [Fact]
    public async Task CancellingADragOnAnOverlappingEdgeDoesNotDetachIt()
    {
        var desktop = TwoDisplays();
        desktop.ParkHidden = true;
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3)]);
        await session.CycleWidthAsync();
        var width = session.Strips["left"].Columns.Single(item => item.Identity == Id(2)).Width;
        var start = new DesktopWindowEvent(2, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start);
        await session.ReconcileAsync([start]);
        desktop.FocusedHandle = 2;
        await session.ReconcileAsync([new(2, DesktopWindowEventKind.MoveSizeEnded)]);
        Assert.Equal(Id(2), session.Strip!.Focused);
        Assert.Equal(width, session.Strip.Columns.Single(item => item.Identity == Id(2)).Width);
        Assert.Equal(3, desktop.Store.Journal!.Windows.Length);
        Assert.True(MonitorPlacement.Contains(session.Strip.Viewport, desktop.Moves[2]));
    }

    [Fact]
    public async Task OversizedMinimumRestoresInsteadOfParkingTheFocusedWindow()
    {
        var desktop = TwoDisplays();
        desktop.ParkHidden = true;
        desktop.MinimumWidth = 1700;
        var session = Session(desktop);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync([Id(1), Id(3)]));
        Assert.False(session.Running);
        Assert.Equal(2, session.LastRestore!.Restored);
        Assert.Empty(desktop.Store.Journal!.Windows);
    }

    private static Desktop TwoDisplays()
    {
        var desktop = new Desktop
        {
            Configuration = [new("left", new(-1600, 0, 1600, 1000), new(-1600, 0, 1600, 960), 96, false),
                new("right", new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), 144, true)],
        };
        desktop.WindowDisplays[1] = desktop.WindowDisplays[2] = "left";
        desktop.WindowDisplays[3] = desktop.WindowDisplays[4] = "right";
        return desktop;
    }

    [Fact]
    public async Task NavigationAndWidthChangesLeaveTheOtherDisplayUntouched()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3), Id(4)]);
        var other = session.Strips["right"].Layout();
        var columns = session.Strips["right"].Columns.ToArray();
        desktop.MoveCalls.Clear();
        await session.CycleWidthAsync();
        await session.NavigateAsync(1);
        Assert.All(desktop.MoveCalls, call => Assert.Equal("left", call.Display));
        Assert.Equal(other, session.Strips["right"].Layout());
        Assert.Equal(columns, session.Strips["right"].Columns);
        Assert.All(other, target => Assert.Equal(target.Bounds, desktop.Moves[target.Identity.Handle]));
    }

    [Fact]
    public async Task ForegroundWindowRoutesCommandsToItsOwnedDisplay()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3), Id(4)]);
        desktop.FocusedHandle = 3;
        desktop.MoveCalls.Clear();
        await session.NavigateAsync(1);
        Assert.Equal("right", session.ActiveDisplayId);
        Assert.Equal(Id(4), session.Strip!.Focused);
        Assert.All(desktop.MoveCalls, call => Assert.Equal("right", call.Display));
    }

    [Fact]
    public async Task BackgroundWindowJoinsOnlyItsOwnDisplayWithoutStealingOtherFocus()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)], automaticEnrollment: true);
        var first = session.Strips["left"].Layout();
        desktop.Eligible.Add(Id(5)); desktop.WindowDisplays[5] = "right";
        desktop.MoveCalls.Clear();
        await session.ReconcileAsync([new(5, DesktopWindowEventKind.Shown)]);
        Assert.Equal("left", session.ActiveDisplayId);
        Assert.Equal(Id(3), session.Strips["right"].Focused);
        Assert.Equal(first, session.Strips["left"].Layout());
        Assert.All(desktop.MoveCalls, call => Assert.Equal("right", call.Display));
        Assert.Equal(3, desktop.Store.Journal!.Windows.Length);
    }

    [Fact]
    public async Task TransferPreservesTheOriginalCaptureAndDestinationWidths()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        await session.CycleWidthAsync();
        var original = desktop.Store.Journal!.Windows.Single(item => item.Identity == Id(1));
        var otherWidth = session.Strips["right"].Columns[0].Width;
        await session.TransferFocusedAsync("right");
        Assert.Empty(session.Strips["left"].Columns);
        Assert.Equal(otherWidth, session.Strips["right"].Columns.Single(item => item.Identity == Id(3)).Width);
        var transferred = session.Strips["right"].Columns.Single(item => item.Identity == Id(1));
        Assert.Equal(ColumnWidths.ResolvePixels(ColumnWidth.TwoThirds, session.Strips["right"].Viewport.Width, session.Strips["right"].Gap), transferred.Width);
        Assert.Same(original, desktop.Store.Journal.Windows.Single(item => item.Identity == Id(1)));
        await session.PauseAsync();
        Assert.Equal(2, session.LastRestore!.Restored);
    }

    [Fact]
    public async Task CustomWidthTransferScalesByDestinationDpi()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start); await session.ReconcileAsync([start]);
        desktop.Moves[1] = desktop.Moves[1] with { Width = 700 };
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.MoveSizeEnded)]);
        await session.TransferFocusedAsync("right");
        Assert.Equal(1050, session.Strips["right"].Columns.Single(item => item.Identity == Id(1)).Width);
    }

    [Fact]
    public async Task ManualCrossDisplayDragTransfersRatherThanDetaches()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        var start = new DesktopWindowEvent(1, DesktopWindowEventKind.MoveSizeStarted);
        session.Observe(start); await session.ReconcileAsync([start]);
        desktop.Moves[1] = desktop.Moves[1] with { Left = 200 };
        desktop.WindowDisplays[1] = "right";
        await session.ReconcileAsync([new(1, DesktopWindowEventKind.MoveSizeEnded)]);
        Assert.Empty(session.Strips["left"].Columns);
        Assert.Contains(session.Strips["right"].Columns, item => item.Identity == Id(1));
        Assert.Equal(2, desktop.Store.Journal!.Windows.Length);
    }

    [Fact]
    public async Task RemovingOneDisplayPausesAndRestoresBothDisplays()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        desktop.Configuration = [desktop.Configuration![1]];
        desktop.MoveCalls.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReconcileAsync());
        Assert.False(session.Running);
        Assert.Equal(2, session.LastRestore!.Restored);
        Assert.Empty(desktop.MoveCalls);
    }

    [Fact]
    public async Task NegativeOriginsAndIndependentScaleProducePhysicalViewports()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        Assert.Equal(new PixelRect(-1592, 8, 1584, 944), session.Strips["left"].Viewport);
        Assert.Equal(new PixelRect(12, 12, 2536, 1376), session.Strips["right"].Viewport);
        Assert.Equal(12, session.Strips["left"].Gap);
        Assert.Equal(18, session.Strips["right"].Gap);
    }

    [Fact]
    public async Task MinimizedWindowReturnsToItsOwnedStripWithoutRecapture()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3), Id(4)]);
        var saved = session.Strips["right"].Columns.ToArray();
        desktop.Excluded.Add(3); await session.ReconcileAsync();
        desktop.Excluded.Remove(3); await session.ReconcileAsync();
        Assert.Equal(saved, session.Strips["right"].Columns);
        Assert.Equal(3, desktop.Store.Journal!.Windows.Length);
    }

    [Fact]
    public async Task ExplicitMonitorSelectionIsNotOverriddenByUnchangedForeground()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(3)]);
        await session.SetActiveDisplayAsync("right");
        desktop.MoveCalls.Clear();
        await session.CycleWidthAsync();
        Assert.Equal("right", session.ActiveDisplayId);
        Assert.All(desktop.MoveCalls, call => Assert.Equal("right", call.Display));
    }

    [Fact]
    public async Task TransferShortcutChoosesDestinationAfterFollowingForeground()
    {
        var desktop = TwoDisplays();
        var session = Session(desktop);
        await session.StartAsync([Id(1), Id(2), Id(3)]);
        await session.SetActiveDisplayAsync("right");
        desktop.FocusedHandle = 2;
        await session.TransferFocusedToNextDisplayAsync();
        Assert.Equal(Id(1), Assert.Single(session.Strips["left"].Columns).Identity);
        Assert.Contains(session.Strips["right"].Columns, item => item.Identity == Id(2));
    }
}
