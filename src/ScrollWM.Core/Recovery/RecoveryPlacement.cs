using ScrollWM.Core.Windows;

namespace ScrollWM.Core.Recovery;

public static class RecoveryPlacement
{
    // Preserve exact workspace coordinates when the original display is unchanged.
    // Otherwise center the original logical size on that display, or the primary
    // display when it is absent. Never reuse disconnected-screen coordinates.
    public static SavedPlacement Resolve(WindowSnapshot snapshot, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) { throw new InvalidOperationException("No display is available for restoration."); }
        var original = snapshot.OriginalDisplay;
        if (original is null)
        {
            if (displays.Any(display => Intersects(snapshot.VisibleFrame, display.WorkArea))) { return snapshot.Placement; }
        }
        else if (displays.Contains(original)) { return snapshot.Placement; }

        var target = displays.FirstOrDefault(display => display.Id == original?.Id)
            ?? displays.FirstOrDefault(display => display.Primary) ?? displays[0];
        var ratio = target.Dpi / (double)(original?.Dpi ?? (snapshot.Dpi == 0 ? 96 : snapshot.Dpi));
        var width = Math.Clamp((int)Math.Round(snapshot.Placement.NormalBounds.Width * ratio), 1, target.WorkArea.Width);
        var height = Math.Clamp((int)Math.Round(snapshot.Placement.NormalBounds.Height * ratio), 1, target.WorkArea.Height);
        var screenLeft = target.WorkArea.Left + (target.WorkArea.Width - width) / 2;
        var screenTop = target.WorkArea.Top + (target.WorkArea.Height - height) / 2;
        var workspaceLeft = screenLeft - (target.WorkArea.Left - target.Bounds.Left);
        var workspaceTop = screenTop - (target.WorkArea.Top - target.Bounds.Top);
        return snapshot.Placement with { NormalBounds = new(workspaceLeft, workspaceTop, width, height) };
    }

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.Left < second.Right && second.Left < first.Right && first.Top < second.Bottom && second.Top < first.Bottom;
}
