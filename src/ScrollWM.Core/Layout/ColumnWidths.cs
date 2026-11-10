namespace ScrollWM.Core.Layout;

public static class ColumnWidths
{
    // The caller removes outer margins from the work-area width before resolving.
    // App minimum sizes are enforced later by the native layout coordinator.
    public static int ResolvePixels(ColumnWidth preset, int usableWidth, int gap = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(usableWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);
        var pairWidth = Math.Max(1L, (long)usableWidth - gap);
        var tripleWidth = Math.Max(1L, (long)usableWidth - 2L * gap);

        var width = preset switch
        {
            ColumnWidth.Third => (int)(tripleWidth / 3),
            ColumnWidth.Half => (int)(pairWidth / 2),
            ColumnWidth.TwoThirds => (int)(pairWidth * 2 / 3),
            ColumnWidth.Full => usableWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown width preset."),
        };

        return Math.Max(1, width);
    }
}
