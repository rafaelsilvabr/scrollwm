namespace ScrollWM.Core.Windows;

public static class MonitorPlacement
{
    // Keep edge windows inside their monitor without resizing. Fully hidden
    // columns park outside the desktop; logical strip coordinates do not change.
    public static PixelRect PhysicalTarget(PixelRect logical, PixelRect viewport, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) { throw new ArgumentException("At least one monitor is required.", nameof(displays)); }
        if (logical.Width <= 0 || logical.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
        { throw new ArgumentException("Window and viewport sizes must be positive."); }
        if (logical.Width > viewport.Width || logical.Height > viewport.Height)
        { throw new InvalidOperationException("The window is too large to fit on its monitor without clipping."); }
        if (Intersects(logical, viewport))
        {
            return logical with { Left = Math.Clamp(logical.Left, viewport.Left, viewport.Right - logical.Width),
                Top = Math.Clamp(logical.Top, viewport.Top, viewport.Bottom - logical.Height) };
        }
        var owner = displays.FirstOrDefault(item => viewport.Left >= item.Bounds.Left && viewport.Top >= item.Bounds.Top &&
            viewport.Right <= item.Bounds.Right && viewport.Bottom <= item.Bounds.Bottom)
            ?? throw new ArgumentException("The viewport must belong to a connected monitor.", nameof(viewport));
        PixelRect[] candidates =
        [logical with { Left = checked(displays.Max(item => item.Bounds.Right) + 64), Top = viewport.Top },
         logical with { Left = checked(displays.Min(item => item.Bounds.Left) - logical.Width - 64), Top = viewport.Top },
         logical with { Left = viewport.Left, Top = checked(displays.Min(item => item.Bounds.Top) - logical.Height - 64) },
         logical with { Left = viewport.Left, Top = checked(displays.Max(item => item.Bounds.Bottom) + 64) }];
        // Prefer an exterior edge nearest the owning display so parking does
        // not needlessly send a window to a neighbor with a different DPI.
        var ordered = candidates.OrderBy(item => DistanceSquared(item, owner.Bounds)).ToArray();
        return ordered.FirstOrDefault(candidate => displays.All(display =>
            DistanceSquared(candidate, owner.Bounds) <= DistanceSquared(candidate, display.Bounds)), ordered[0]);
    }

    public static bool Intersects(PixelRect first, PixelRect second) =>
        first.Left < second.Right && second.Left < first.Right && first.Top < second.Bottom && second.Top < first.Bottom;

    public static bool Contains(PixelRect container, PixelRect rectangle) => rectangle.Left >= container.Left &&
        rectangle.Top >= container.Top && rectangle.Right <= container.Right && rectangle.Bottom <= container.Bottom;

    // Leave space for invisible resize borders as well as the visible frame.
    public static int Margin(DisplayInfo display, int displayCount) => display.Scale(displayCount > 1 ? 8 : 6);

    // DWM can report outer bounds for a parked custom-frame window. Use the
    // known frame-to-outer offsets; retain real size and position changes.
    public static PixelRect FrameFromOuter(PixelRect actualOuter, PixelRect requestedOuter, PixelRect requestedFrame) =>
        new(checked(actualOuter.Left + requestedFrame.Left - requestedOuter.Left),
            checked(actualOuter.Top + requestedFrame.Top - requestedOuter.Top),
            checked(actualOuter.Width - requestedOuter.Width + requestedFrame.Width),
            checked(actualOuter.Height - requestedOuter.Height + requestedFrame.Height));

    private static long DistanceSquared(PixelRect first, PixelRect second)
    {
        var x = Math.Max(0L, Math.Max((long)first.Left - second.Right, (long)second.Left - first.Right));
        var y = Math.Max(0L, Math.Max((long)first.Top - second.Bottom, (long)second.Top - first.Bottom));
        return checked(x * x + y * y);
    }
}
