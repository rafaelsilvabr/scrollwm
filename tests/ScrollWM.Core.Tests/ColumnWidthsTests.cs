using ScrollWM.Core.Layout;
using Xunit;

namespace ScrollWM.Core.Tests;

public class ColumnWidthsTests
{
    [Theory]
    [InlineData(ColumnWidth.Half, 2)]
    [InlineData(ColumnWidth.Third, 3)]
    public void PresetsLeaveSpaceForGapsSoWholeWindowsFit(ColumnWidth preset, int columns)
    {
        var width = ColumnWidths.ResolvePixels(preset, 1904, 12);
        Assert.InRange(width * columns + 12 * (columns - 1), 1902, 1904);
    }
    [Theory]
    [InlineData(ColumnWidth.Third, 400)]
    [InlineData(ColumnWidth.Half, 600)]
    [InlineData(ColumnWidth.TwoThirds, 800)]
    [InlineData(ColumnWidth.Full, 1200)]
    public void ResolvesPresetAgainstUsableWidth(ColumnWidth preset, int expected)
    {
        Assert.Equal(expected, ColumnWidths.ResolvePixels(preset, 1200));
    }

    [Fact]
    public void FractionalWidthsRoundDownToPhysicalPixels()
    {
        Assert.Equal(333, ColumnWidths.ResolvePixels(ColumnWidth.Third, 1000));
        Assert.Equal(666, ColumnWidths.ResolvePixels(ColumnWidth.TwoThirds, 1000));
    }

    [Fact]
    public void NarrowViewportNeverProducesAZeroWidth()
    {
        Assert.Equal(1, ColumnWidths.ResolvePixels(ColumnWidth.Third, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidUsableWidth(int usableWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnWidths.ResolvePixels(ColumnWidth.Half, usableWidth));
    }

    [Fact]
    public void RejectsUnknownPreset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnWidths.ResolvePixels((ColumnWidth)99, 1200));
    }

    [Fact]
    public void LargeValidWidthsDoNotOverflow()
    {
        Assert.Equal(1431655764, ColumnWidths.ResolvePixels(ColumnWidth.TwoThirds, int.MaxValue));
    }
}
