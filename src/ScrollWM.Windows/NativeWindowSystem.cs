using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Management;
using ScrollWM.Core.Windows;
using ScrollWM.Windows.Native;

namespace ScrollWM.Windows;

public sealed record WindowCandidate(WindowIdentity Identity, string ProcessName, PixelRect Frame, uint Dpi)
{
    public string Label => $"{ProcessName} · PID {Identity.ProcessId} · HWND {Identity.Handle} · {Dpi} DPI";
    public override string ToString() => Label;
}

public sealed record DisplayState(int Count, PixelRect WorkArea);

public sealed record WindowInspection(
    WindowIdentity Identity, string ProcessName, PixelRect Frame, uint Dpi, string? ExclusionReason, SavedPlacement Placement);

public sealed partial class NativeWindowSystem : IStripDesktop
{
    private const string TokenProperty = "ScrollWM.RecoveryToken.v1";

    public static void MarkTestFixture(nint handle)
    {
        NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        if (pid != Environment.ProcessId || !NativeMethods.SetProp(handle, "ScrollWM.TestFixture.v1", 1))
        {
            throw new InvalidOperationException("Only this process's disposable windows can be marked as test fixtures.");
        }
    }

    public static bool IsTestFixture(long handle) => NativeMethods.GetProp((nint)handle, "ScrollWM.TestFixture.v1") == 1;

    public static void CloseTestFixture(long handle)
    {
        if (!IsTestFixture(handle) || !NativeMethods.PostMessage((nint)handle, 0x0010, 0, 0))
        {
            throw new InvalidOperationException("Only a marked disposable fixture may be closed by the verification tool.");
        }
    }

    public static void MinimizeTestFixture(long handle, bool minimized)
    {
        if (!IsTestFixture(handle)) { throw new InvalidOperationException("Only a marked disposable fixture may be changed by the verification tool."); }
        NativeMethods.ShowWindow((nint)handle, minimized ? 7 : 4);
    }

    public static void PositionTestFixture(long handle, PixelRect rectangle)
    {
        if (!IsTestFixture(handle)) { throw new InvalidOperationException("Only a marked disposable fixture may be positioned by the verification tool."); }
        if (!NativeMethods.SetWindowPos((nint)handle, 0, rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height,
            0x0004 | 0x0010 | 0x0200)) { throw NativeError("Could not position a disposable fixture"); }
    }

    public NativeWindowSystem()
    {
        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            throw new PlatformNotSupportedException("The experiment requires Windows 11 and a 64-bit process.");
        }
    }

    public DisplayState GetDisplays()
    {
        var monitors = new List<PixelRect>();
        NativeMethods.MonitorCallback callback = (nint monitor, nint _, ref NativeMethods.Rect rectangle, nint parameter) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            monitors.Add(Convert(info.Work));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0) || monitors.Count == 0)
        {
            throw new InvalidOperationException("Could not read the active display work area.");
        }

        return new(monitors.Count, monitors[0]);
    }

    public void RequireSingleDisplay()
    {
        if (GetDisplays().Count != 1)
        {
            throw new InvalidOperationException("This experiment requires exactly one active display. Restoration remains available.");
        }
    }

    public PixelRect GetSingleDisplayWorkArea()
    {
        var displays = GetDisplays();
        if (displays.Count != 1)
        {
            throw new InvalidOperationException("The experimental strip requires exactly one active display.");
        }

        return displays.WorkArea;
    }

    public bool IsEligible(WindowSnapshot snapshot) =>
        CheckIdentity(snapshot) == IdentityStatus.Matches && ExclusionReason((nint)snapshot.Identity.Handle) is null;

    public uint ReadDpi(WindowSnapshot snapshot) => NativeMethods.GetDpiForWindow((nint)snapshot.Identity.Handle);

    public bool IsUserMoving(WindowSnapshot snapshot)
    {
        var information = new NativeMethods.GuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>() };
        return !NativeMethods.GetGUIThreadInfo(snapshot.Identity.ThreadId, ref information) ||
            (information.Flags & 2) != 0 && information.MoveSize.ToInt64() == snapshot.Identity.Handle;
    }

    public IReadOnlyList<WindowCandidate> EnumerateCandidates() => InspectApplications()
        .Where(item => item.ExclusionReason is null)
        .Select(item => new WindowCandidate(item.Identity, item.ProcessName, item.Frame, item.Dpi)).ToArray();

    public IReadOnlyList<WindowIdentity> EnumerateKnownWindows()
    {
        var known = new List<WindowIdentity>();
        NativeMethods.EnumWindowCallback callback = (handle, _) =>
        {
            try
            {
                var identity = ReadIdentity(handle);
                if (identity.ProcessId != Environment.ProcessId) { known.Add(identity); }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
            {
                // Baseline hidden and ineligible windows too, when their identity is accessible.
            }
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, 0)) { throw new InvalidOperationException("Window enumeration failed."); }
        return known;
    }
    public IReadOnlyList<WindowIdentity> EnumerateEligibleWindows() => EnumerateCandidates().Select(item => item.Identity).ToArray();

    public IReadOnlyList<WindowInspection> InspectApplications()
    {
        var result = new List<WindowInspection>();
        var foreground = NativeMethods.GetForegroundWindow().ToInt64();
        NativeMethods.EnumWindowCallback callback = (handle, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindow(handle, 4) != 0)
                {
                    return true;
                }

                var identity = ReadIdentity(handle);
                if (identity.ProcessId == Environment.ProcessId)
                {
                    return true;
                }

                using var process = Process.GetProcessById(identity.ProcessId);
                var reason = ExclusionReason(handle);
                if (reason is null && !IsNonElevated((uint)identity.ProcessId))
                {
                    reason = "Elevated or inaccessible process";
                }

                result.Add(new(identity, process.ProcessName, ReadFrame(identity.Handle), NativeMethods.GetDpiForWindow(handle), reason,
                    ReadPlacement(handle)));
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
            {
                // An exiting or inaccessible application is not a candidate.
            }

            return true;
        };

        if (!NativeMethods.EnumWindows(callback, 0))
        {
            throw new InvalidOperationException("Window enumeration failed.");
        }

        return result.OrderByDescending(item => item.Identity.Handle == foreground).ToArray();
    }

    public WindowSnapshot Capture(WindowIdentity identity) => CaptureWindow(identity, allowMaximized: false);

    // Explicit diagnostic-only entry point. Normal management continues to
    // exclude maximized windows; the probe journals their original show state.
    public WindowSnapshot CaptureForProbe(WindowIdentity identity) => CaptureWindow(identity, allowMaximized: true);

    private WindowSnapshot CaptureWindow(WindowIdentity identity, bool allowMaximized)
    {
        var handle = (nint)identity.Handle;
        try
        {
            if (ExclusionReason(handle, allowMaximized) is not null || ReadIdentity(handle) != identity ||
                !IsNonElevated((uint)identity.ProcessId))
            {
                throw new WindowUnavailableException("The selected window changed or is no longer eligible. Refresh the list.");
            }

            var token = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            if (!NativeMethods.SetProp(handle, TokenProperty, token))
            {
                throw NativeError("Could not attach a recovery identity to the window");
            }

            try
            {
                var placement = ReadPlacement(handle);
                var frame = ReadFrame(identity.Handle);
                var region = ReadWindowRegion(handle);
                var displayId = GetWindowDisplay(identity);
                var originalDisplay = GetDisplayConfiguration().Single(item => item.Id == displayId);
                if (!NativeMethods.GetWindowRect(handle, out var outer)) { throw NativeError("Could not capture outer window bounds"); }
                if (NativeMethods.GetProp(handle, TokenProperty).ToInt64() != token || ReadIdentity(handle) != identity || ReadPlacement(handle) != placement)
                { throw new WindowUnavailableException("The window changed during capture. Refresh and retry."); }
                return new(identity, placement, frame, NativeMethods.GetDpiForWindow(handle), token, region, originalDisplay, Convert(outer));
            }
            catch
            {
                if (NativeMethods.GetProp(handle, TokenProperty).ToInt64() == token) { NativeMethods.RemoveProp(handle, TokenProperty); }
                throw;
            }
        }
        catch (Exception exception) when ((exception is Win32Exception or InvalidOperationException or ArgumentException) &&
            !NativeMethods.IsWindow(handle))
        {
            throw new WindowUnavailableException("The selected window closed before it could be captured.");
        }
    }

    public void PrepareForProbe(WindowSnapshot snapshot)
    {
        RequireSingleDisplay();
        RequireIdentity(snapshot);
        if (snapshot.Placement.ShowCommand == 3)
        {
            // SW_SHOWNOACTIVATE restores normal geometry. The caller must have
            // durably journaled the original maximized placement first.
            var normal = ToNativePlacement(snapshot.Placement with { ShowCommand = 4 });
            if (!NativeMethods.SetWindowPlacement((nint)snapshot.Identity.Handle, in normal))
            {
                throw NativeError("Could not prepare the maximized window for the diagnostic probe");
            }
        }
    }

    public IdentityStatus CheckIdentity(WindowSnapshot snapshot)
    {
        var handle = (nint)snapshot.Identity.Handle;
        if (!NativeMethods.IsWindow(handle))
        {
            return IdentityStatus.ClosedOrReused;
        }

        try
        {
            if (ReadIdentity(handle) != snapshot.Identity ||
                NativeMethods.GetProp(handle, TokenProperty).ToInt64() != snapshot.RecoveryToken)
            {
                return IdentityStatus.ClosedOrReused;
            }

            return IdentityStatus.Matches;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return IdentityStatus.Unavailable;
        }
    }

    public void Move(WindowSnapshot snapshot, PixelRect target)
    {
        RequireSingleDisplay();
        MoveFrame(snapshot, target);
    }

    private void MoveFrame(WindowSnapshot snapshot, PixelRect target)
    {
        RequireIdentity(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target.Height);
        var handle = (nint)snapshot.Identity.Handle;
        var frame = ReadFrame(snapshot.Identity.Handle);
        if (!NativeMethods.GetWindowRect(handle, out var outer))
        {
            throw NativeError("Could not read outer window bounds");
        }

        var leftBorder = frame.Left - outer.Left;
        var topBorder = frame.Top - outer.Top;
        var rightBorder = outer.Right - frame.Right;
        var bottomBorder = outer.Bottom - frame.Bottom;
        // Do not activate or reorder another app as a side effect of positioning.
        if (!NativeMethods.SetWindowPos(handle, 0, target.Left - leftBorder, target.Top - topBorder,
            target.Width + leftBorder + rightBorder, target.Height + topBorder + bottomBorder,
            0x0004 | 0x0010 | 0x0200))
        {
            throw NativeError("Window positioning failed");
        }
    }

    public bool Focus(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        return NativeMethods.SetForegroundWindow((nint)snapshot.Identity.Handle);
    }

    public long FocusedHandle => NativeMethods.GetForegroundWindow().ToInt64();

    public PixelRect ReadFrame(long handle)
    {
        if (NativeMethods.GetDwmRect((nint)handle, 9, out var frame, Marshal.SizeOf<NativeMethods.Rect>()) == 0)
        {
            return Convert(frame);
        }

        if (!NativeMethods.GetWindowRect((nint)handle, out var rectangle))
        {
            throw NativeError("Could not read the window frame");
        }

        return Convert(rectangle);
    }

    public void Restore(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        var saved = ScrollWM.Core.Recovery.RecoveryPlacement.Resolve(snapshot, GetDisplayConfiguration());
        var placement = ToNativePlacement(saved);

        if (!NativeMethods.SetWindowPlacement((nint)snapshot.Identity.Handle, in placement))
        {
            throw NativeError("Window restoration failed");
        }

        // WINDOWPLACEMENT's normal bounds and show state must be observable
        // before the journal is allowed to forget this window.
        var actual = ReadPlacement((nint)snapshot.Identity.Handle);
        if (actual.ShowCommand != saved.ShowCommand || actual.NormalBounds != saved.NormalBounds)
        {
            throw new InvalidOperationException("Windows adjusted the restored placement. The recovery entry was retained for retry.");
        }
        RestorePresentation(snapshot);
    }

    private static NativeMethods.WindowPlacement ToNativePlacement(SavedPlacement saved) =>
        new()
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>(),
            Flags = saved.Flags,
            ShowCommand = saved.ShowCommand,
            MinPosition = new() { X = saved.MinPosition.X, Y = saved.MinPosition.Y },
            MaxPosition = new() { X = saved.MaxPosition.X, Y = saved.MaxPosition.Y },
            NormalPosition = new()
            {
                Left = saved.NormalBounds.Left,
                Top = saved.NormalBounds.Top,
                Right = saved.NormalBounds.Right,
                Bottom = saved.NormalBounds.Bottom,
            },
        };

    public void ReleaseToken(WindowSnapshot snapshot)
    {
        projectedFrames.Remove(snapshot.Identity);
        if (NativeMethods.GetProp((nint)snapshot.Identity.Handle, TokenProperty).ToInt64() == snapshot.RecoveryToken)
        {
            NativeMethods.RemoveProp((nint)snapshot.Identity.Handle, TokenProperty);
        }
    }

    private void RequireIdentity(WindowSnapshot snapshot)
    {
        if (CheckIdentity(snapshot) != IdentityStatus.Matches)
        {
            throw new InvalidOperationException("Window identity no longer matches the captured window; no change was applied.");
        }
    }

    private static string? ExclusionReason(nint handle, bool allowMaximized = false)
    {
        if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindow(handle, 4) != 0)
        {
            return "Hidden or owned window";
        }

        if (NativeMethods.IsIconic(handle))
        {
            return "Minimized";
        }

        if (!allowMaximized && NativeMethods.IsZoomed(handle))
        {
            return "Maximized";
        }

        var style = NativeMethods.GetWindowLongPtr(handle, -16).ToInt64();
        var extended = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
        if ((style & 0x00040000) == 0 || (style & (0x40000000 | 0x08000000)) != 0 ||
            (extended & (0x00000080 | 0x08000000)) != 0)
        {
            return "Not a normal resizable application window";
        }

        if (NativeMethods.GetDwmInt(handle, 14, out var cloaked, sizeof(int)) != 0)
        {
            return "Cloaking state unavailable";
        }

        return cloaked == 0 ? null : "Cloaked or on another desktop";
    }

    private static WindowIdentity ReadIdentity(nint handle)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out var pid);
        if (threadId == 0)
        {
            throw NativeError("Could not read window identity");
        }

        var name = new StringBuilder(256);
        if (NativeMethods.GetClassName(handle, name, name.Capacity) == 0)
        {
            throw NativeError("Could not read window class");
        }

        using var process = Process.GetProcessById((int)pid);
        return new(handle.ToInt64(), (int)pid, process.StartTime.ToUniversalTime().Ticks, threadId, name.ToString());
    }

    private static SavedPlacement ReadPlacement(nint handle)
    {
        var placement = new NativeMethods.WindowPlacement { Length = (uint)Marshal.SizeOf<NativeMethods.WindowPlacement>() };
        if (!NativeMethods.GetWindowPlacement(handle, ref placement))
        {
            throw NativeError("Could not capture original window placement");
        }

        return new(placement.Flags, placement.ShowCommand,
            new(placement.MinPosition.X, placement.MinPosition.Y),
            new(placement.MaxPosition.X, placement.MaxPosition.Y), Convert(placement.NormalPosition));
    }

    private static bool IsNonElevated(uint pid)
    {
        var process = NativeMethods.OpenProcess(0x1000, false, pid);
        if (process == 0)
        {
            return false;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, 0x0008, out var token))
            {
                return false;
            }

            try
            {
                return NativeMethods.GetTokenInformation(token, 20, out var elevated, sizeof(int), out _) && elevated == 0;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static PixelRect Convert(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    private static Win32Exception NativeError(string message) => new(Marshal.GetLastWin32Error(), message);
}
