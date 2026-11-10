namespace ScrollWM.Core.Windows;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}

// Process start time guards PID reuse; the recovery token additionally guards
// HWND reuse within the same process. Titles and document names are never stored.
public sealed record WindowIdentity(
    long Handle, int ProcessId, long ProcessStartUtcTicks, uint ThreadId, string ClassName);

// NormalBounds uses WINDOWPLACEMENT workspace coordinates, not screen pixels.
public sealed record SavedPlacement(
    uint Flags, uint ShowCommand, PixelPoint MinPosition, PixelPoint MaxPosition, PixelRect NormalBounds);

public sealed record WindowSnapshot(
    WindowIdentity Identity, SavedPlacement Placement, PixelRect VisibleFrame, uint Dpi, long RecoveryToken,
    byte[]? OriginalRegion = null, DisplayInfo? OriginalDisplay = null, PixelRect? OriginalOuterFrame = null);

public sealed record DisplayInfo(string Id, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool Primary)
{
    public int Scale(int pixels) => Math.Max(1, checked((int)Math.Round(pixels * Dpi / 96d)));
}

public enum IdentityStatus
{
    Matches,
    ClosedOrReused,
    Unavailable,
}

public sealed class WindowUnavailableException(string message) : InvalidOperationException(message);
