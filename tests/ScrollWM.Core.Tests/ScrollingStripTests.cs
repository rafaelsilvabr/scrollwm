using ScrollWM.Core.Layout;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public class ScrollingStripTests
{
    [Fact]
    public void FifthWindowInsertsBesideFocusWithoutShrinkingExistingColumns()
    {
        var strip = Create(4);
        strip.Focus(Id(2));
        var widths = strip.Columns.ToDictionary(item => item.Identity, item => item.Width);
        strip.InsertAfterFocus(Id(5));

        Assert.Equal(new long[] { 1, 2, 5, 3, 4 }, strip.Columns.Select(item => item.Identity.Handle));
        Assert.All(strip.Columns.Where(item => widths.ContainsKey(item.Identity)), item => Assert.Equal(widths[item.Identity], item.Width));
        Assert.Equal(Id(5), strip.Focused);
        var focused = strip.Layout().Single(item => item.Identity == Id(5)).Bounds;
        Assert.True(focused.Left >= strip.Viewport.Left && focused.Right <= strip.Viewport.Right);
    }

    [Fact]
    public void FocusUsesMinimalScrollAndStopsAtBothEnds()
    {
        var strip = Create(3);
        strip.Focus(Id(1));
        Assert.Equal(0, strip.Offset);
        Assert.False(strip.FocusAdjacent(-1));
        strip.FocusAdjacent(1);
        Assert.Equal(0, strip.Offset);
        strip.FocusAdjacent(1);
        Assert.Equal(606, strip.Offset);
        Assert.False(strip.FocusAdjacent(1));
        Assert.Equal(Id(3), strip.Focused);
        strip.Focus(Id(1));
        Assert.Equal(0, strip.Offset);
    }

    [Fact]
    public void ReorderingRetainsFocusAndWidthsWhileChangingNeighbors()
    {
        var strip = Create(3);
        strip.Focus(Id(2));
        strip.CycleWidth();
        var width = strip.Columns[1].Width;
        Assert.True(strip.ReorderFocused(-1));
        Assert.False(strip.ReorderFocused(-1));
        Assert.Equal(new long[] { 2, 1, 3 }, strip.Columns.Select(item => item.Identity.Handle));
        Assert.Equal(Id(2), strip.Focused);
        Assert.Equal(width, strip.Columns[0].Width);
    }

    [Fact]
    public void ClosingFocusedWindowChoosesNeighborAndClosesGap()
    {
        var strip = Create(3);
        strip.Focus(Id(2));
        strip.Remove(Id(2));
        Assert.Equal(Id(3), strip.Focused);
        Assert.Equal(0, strip.Offset);
        strip.Remove(Id(3));
        Assert.Equal(Id(1), strip.Focused);
        Assert.Equal(0, strip.Offset);
        strip.Remove(Id(1));
        Assert.Null(strip.Focused);
        Assert.Empty(strip.Layout());
        Assert.False(strip.FocusAdjacent(1));
    }

    [Fact]
    public void OversizedMinimumAlignsAtLeftAndSurvivesWidthCycling()
    {
        var strip = Create(2);
        strip.SetMinimumWidth(Id(2), 1600);
        strip.CycleWidth();
        Assert.Equal(1600, strip.Columns[1].Width);
        Assert.Equal(strip.Viewport.Left, strip.Layout()[1].Bounds.Left);
        Assert.Equal(594, strip.Columns[0].Width);
    }

    [Fact]
    public void NegativeScreenOriginMarginsAndCustomWidthAreRespected()
    {
        var strip = new ScrollingStrip(new(-1400, -200, 1200, 900));
        strip.InsertAfterFocus(Id(1));
        strip.SetCustomWidth(Id(1), 777);
        Assert.Equal(new PixelRect(-1394, -194, 777, 888), Assert.Single(strip.Layout()).Bounds);
        strip.CycleWidth();
        Assert.Equal(588, strip.Columns[0].Width);
    }

    [Fact]
    public void HandleReuseRequiresRemovingOldIdentityFirst()
    {
        var strip = Create(1);
        var reused = Id(1) with { ProcessStartUtcTicks = 999 };
        Assert.Throws<ArgumentException>(() => strip.InsertAfterFocus(reused));
        strip.Remove(Id(1));
        strip.InsertAfterFocus(reused);
        Assert.Equal(reused, strip.Focused);
    }

    private static ScrollingStrip Create(int count)
    {
        var strip = new ScrollingStrip(new(0, 0, 1200, 900), margin: 0);
        for (var index = 1; index <= count; index++)
        {
            strip.InsertAfterFocus(Id(index));
        }

        return strip;
    }

    private static WindowIdentity Id(long handle) => new(handle, 10, 123, 20, "Fixture");
}
