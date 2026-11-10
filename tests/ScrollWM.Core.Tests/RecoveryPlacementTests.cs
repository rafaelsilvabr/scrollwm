using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public class RecoveryPlacementTests
{
    private static readonly DisplayInfo Original = new("old", new(1920, 0, 1920, 1080), new(1920, 40, 1920, 1040), 96, false);
    private static WindowSnapshot Snapshot() => new(new(1, 2, 123, 3, "Fixture"),
        new(0, 1, new(-1, -1), new(-1, -1), new(2000, 80, 800, 600)), new(2007, 120, 786, 593), 96, 42, null, Original);

    [Fact]
    public void UnchangedOriginalDisplayPreservesExactWorkspaceCoordinates()
    {
        var snapshot = Snapshot();
        Assert.Equal(snapshot.Placement, RecoveryPlacement.Resolve(snapshot, [Original]));
    }

    [Fact]
    public void MissingDisplayCentersOriginalLogicalSizeOnPrimaryWorkArea()
    {
        var primary = new DisplayInfo("primary", new(-1600, -200, 1600, 1000), new(-1560, -200, 1560, 960), 144, true);
        var result = RecoveryPlacement.Resolve(Snapshot(), [primary]);
        // Workspace coordinates subtract the left taskbar's 40 pixel inset.
        Assert.Equal(new PixelRect(-1420, -170, 1200, 900), result.NormalBounds);
    }

    [Fact]
    public void ReducedResolutionClampsToVisibleWorkAreaOnSameDisplay()
    {
        var target = Original with { Bounds = new(0, 0, 640, 480), WorkArea = new(0, 0, 640, 440) };
        Assert.Equal(new PixelRect(0, 0, 640, 440), RecoveryPlacement.Resolve(Snapshot(), [target]).NormalBounds);
    }

    [Fact]
    public void LegacyJournalOutsideAllDisplaysGetsVisibleFallback()
    {
        var target = new DisplayInfo("primary", new(0, 0, 1200, 900), new(0, 0, 1200, 860), 96, true);
        Assert.Equal(new PixelRect(200, 130, 800, 600), RecoveryPlacement.Resolve(Snapshot() with { OriginalDisplay = null }, [target]).NormalBounds);
    }
}
