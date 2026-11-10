using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public sealed class MonitorClipTests
{
    [Theory]
    [InlineData(0, 0, 800, 600)]
    [InlineData(7, 0, 786, 593)]
    [InlineData(20, 20, 500, 400)]
    public void AcceptsApplicationShapesContainedInThePermittedRectangle(int x, int y, int width, int height) =>
        Assert.True(MonitorClip.Accepts(new(0, 0, 800, 600), new(x, y, width, height)));

    [Theory]
    [InlineData(-1, 0, 800, 600)]
    [InlineData(0, -1, 800, 600)]
    [InlineData(0, 0, 801, 600)]
    [InlineData(0, 0, 800, 601)]
    [InlineData(0, 0, 0, 0)]
    public void RejectsDrawingOutsideTheClipOrUnexpectedlyHiddenWindows(int x, int y, int width, int height) =>
        Assert.False(MonitorClip.Accepts(new(0, 0, 800, 600), new(x, y, width, height)));

    [Fact]
    public void FullyOffViewportColumnsRequireAnEmptyRegion()
    {
        Assert.True(MonitorClip.Accepts(new(0, 0, 0, 0), new(100, 100, 0, 0)));
        Assert.False(MonitorClip.Accepts(new(0, 0, 0, 0), new(0, 0, 1, 1)));
    }

    [Fact]
    public void FullWindowWithoutAnExplicitRegionIsSafeOnlyWhenContained()
    {
        Assert.True(MonitorClip.Accepts(new(-20, 0, 840, 620), new(0, 0, 800, 600)));
        Assert.False(MonitorClip.Accepts(new(400, 0, 400, 600), new(0, 0, 800, 600)));
    }
}
