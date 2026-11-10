namespace ScrollWM.Core.Management;

public enum DesktopWindowEventKind
{
    Foreground,
    Created,
    Destroyed,
    Shown,
    Hidden,
    LocationChanged,
    StateChanged,
    MoveSizeStarted,
    MoveSizeEnded,
    Minimized,
    Restored,
}

public readonly record struct DesktopWindowEvent(long Handle, DesktopWindowEventKind Kind);
