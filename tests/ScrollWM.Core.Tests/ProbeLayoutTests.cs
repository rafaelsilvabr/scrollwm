using ScrollWM.Core.Layout;
using ScrollWM.Core.Windows;
using Xunit;

namespace ScrollWM.Core.Tests;

public class ProbeLayoutTests
{
    [Theory]
    [InlineData(ProbePosition.LeftPartial, -300)]
    [InlineData(ProbePosition.LeftFull, -732)]
    [InlineData(ProbePosition.Center, 500)]
    [InlineData(ProbePosition.RightPartial, 1300)]
    [InlineData(ProbePosition.RightFull, 1732)]
    public void ProbeTargetsRespectWorkAreaOriginAndKeepWidth(ProbePosition position, int expectedLeft)
    {
        var area = new PixelRect(100, 40, 1600, 900);
        var target = ProbeLayout.Target(area, 800, position);
        Assert.Equal(new PixelRect(expectedLeft, 40, 800, 900), target);
    }
}
