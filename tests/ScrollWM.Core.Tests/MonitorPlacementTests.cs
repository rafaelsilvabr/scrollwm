using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public sealed class MonitorPlacementTests
{
    [Fact]
    public void ParkedFrameUsesKnownBordersWhenDwmReportsOuterBounds()
    {
        var outer = new PixelRect(4537, 8, 1286, 1039);
        var frame = new PixelRect(4544, 8, 1272, 1032);
        Assert.Equal(frame, MonitorPlacement.FrameFromOuter(outer, outer, frame));
        Assert.Equal(frame with { Width = 1300 }, MonitorPlacement.FrameFromOuter(outer with { Width = 1314 }, outer, frame));
        Assert.Equal(frame with { Left = 10 }, MonitorPlacement.FrameFromOuter(outer with { Left = 3 }, outer, frame));
    }
    private static readonly DisplayInfo[] Displays =
    [new("primary", new(0, 0, 1920, 1200), new(0, 0, 1920, 1168), 96, true),
     new("secondary", new(1920, 0, 2560, 1080), new(1920, 0, 2560, 1048), 96, false)];
    private static readonly PixelRect Viewport = new(6, 6, 1908, 1156);

    [Theory]
    [InlineData(1926)]
    [InlineData(2892)]
    [InlineData(3858)]
    [InlineData(-960)]
    public void HiddenColumnsNeverParkOnAnyConnectedMonitor(int logicalLeft)
    {
        var logical = new PixelRect(logicalLeft, 6, 954, 1156);
        var physical = MonitorPlacement.PhysicalTarget(logical, Viewport, Displays);
        Assert.Equal(-1018, physical.Left);
        Assert.Equal(logical.Width, physical.Width);
        Assert.Equal(logical.Height, physical.Height);
        Assert.All(Displays, display => Assert.False(MonitorPlacement.Intersects(physical, display.Bounds)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(960)]
    public void FullyVisibleColumnsKeepTheirStripCoordinates(int left)
    {
        var logical = new PixelRect(left, 6, 954, 1156);
        Assert.Equal(logical, MonitorPlacement.PhysicalTarget(logical, Viewport, Displays));
    }

    [Theory]
    [InlineData(972)]
    [InlineData(-400)]
    [InlineData(1800)]
    public void PartialColumnsStayInsideTheirMonitorWithoutChangingSize(int left)
    {
        var physical = MonitorPlacement.PhysicalTarget(new(left, 6, 954, 1156), Viewport, Displays);
        Assert.True(MonitorPlacement.Contains(Viewport, physical));
        Assert.Equal(954, physical.Width);
        Assert.Equal(1156, physical.Height);
        Assert.False(MonitorPlacement.Intersects(physical, Displays[1].Bounds));
    }

    [Theory]
    [InlineData(1909, 1156)]
    [InlineData(954, 1157)]
    public void OversizedWindowsFailInsteadOfLeavingTheFocusedWindowHidden(int width, int height) =>
        Assert.Throws<InvalidOperationException>(() => MonitorPlacement.PhysicalTarget(new(6, 6, width, height), Viewport, Displays));

    [Fact]
    public void NavigationRevealsAParkedColumnWithoutChangingItsWidth()
    {
        var hidden = new PixelRect(1926, 6, 954, 1156);
        Assert.NotEqual(hidden, MonitorPlacement.PhysicalTarget(hidden, Viewport, Displays));
        var revealed = hidden with { Left = 960 };
        Assert.Equal(revealed, MonitorPlacement.PhysicalTarget(revealed, Viewport, Displays));
    }

    [Fact]
    public void ParkingAccountsForNegativeAndVerticallyArrangedMonitors()
    {
        DisplayInfo[] displays =
        [new("left", new(-2560, -1080, 2560, 1080), new(-2560, -1080, 2560, 1048), 144, false),
         new("main", new(0, 0, 1920, 1200), new(0, 0, 1920, 1168), 96, true)];
        var viewport = new PixelRect(-2551, -1071, 2542, 1030);
        var parked = MonitorPlacement.PhysicalTarget(new(20, -1071, 1271, 1030), viewport, displays);
        Assert.Equal(-3895, parked.Left);
        Assert.All(displays, display => Assert.False(MonitorPlacement.Intersects(parked, display.Bounds)));
    }

    [Fact]
    public void SecondaryUsesItsExteriorEdgeInsteadOfParkingBesideThePrimaryDpi()
    {
        var viewport = new PixelRect(1926, 6, 2548, 1036);
        var parked = MonitorPlacement.PhysicalTarget(new(4500, 6, 1274, 1036), viewport, Displays);
        Assert.Equal(4544, parked.Left);
        Assert.Equal(6, parked.Top);
    }
}
