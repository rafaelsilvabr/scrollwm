using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ScrollWM.Core.Windows;
using ScrollWM.Windows.Native;

namespace ScrollWM.Windows;

public sealed partial class NativeWindowSystem
{
    private readonly HashSet<long> placingRegions = [];
    private readonly Dictionary<long, int> regionRepairs = [];
    private sealed record ProjectedFrame(long Token, PixelRect Logical, PixelRect Physical, PixelRect Outer, bool OutsideDesktop);
    private readonly Dictionary<WindowIdentity, ProjectedFrame> projectedFrames = [];
    public static void SetTestFixtureRegion(long window)
    {
        if (!IsTestFixture(window)) { throw new InvalidOperationException("Only a marked disposable fixture may receive a verification region."); }
        if (!NativeMethods.GetWindowRect((nint)window, out var outer)) { throw NativeError("Could not read fixture bounds"); }
        SetRegion((nint)window, MonitorNativeMethods.CreateRoundRectRgn(0, 0, outer.Right - outer.Left, outer.Bottom - outer.Top, 20, 20));
    }
    private List<(nint Handle, DisplayInfo Display)> ReadMonitors()
    {
        var result = new List<(nint Handle, DisplayInfo Display)>();
        Exception? failure = null;
        NativeMethods.MonitorCallback callback = (nint monitor, nint _, ref NativeMethods.Rect rectangle, nint parameter) =>
        {
            try
            {
                var info = new MonitorNativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorNativeMethods.MonitorInfoEx>() };
                if (!MonitorNativeMethods.GetMonitorInfo(monitor, ref info)) { throw NativeError("Could not read monitor information"); }
                var device = new MonitorNativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<MonitorNativeMethods.DisplayDevice>() };
                var id = MonitorNativeMethods.EnumDisplayDevices(info.DeviceName, 0, ref device, 1) && !string.IsNullOrWhiteSpace(device.DeviceId)
                    ? device.DeviceId : "gdi:" + info.DeviceName;
                var status = MonitorNativeMethods.GetScaleFactorForMonitor(monitor, out var scale);
                if (status != 0 || scale <= 0) { throw new InvalidOperationException("Could not read monitor scaling."); }
                result.Add((monitor, new(id, Convert(info.Bounds), Convert(info.Work), checked((uint)(scale * 96 / 100)), (info.Flags & 1) != 0)));
                return true;
            }
            catch (Exception error) { failure = error; return false; }
        };
        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0) || result.Count == 0)
        { throw new InvalidOperationException("Could not read the monitor configuration.", failure); }
        return result;
    }

    public IReadOnlyList<DisplayInfo> GetDisplayConfiguration() => ReadMonitors().Select(item => item.Display)
        .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();

    public string GetWindowDisplay(WindowIdentity identity)
    {
        var monitor = MonitorNativeMethods.MonitorFromWindow((nint)identity.Handle, 2);
        return ReadMonitors().SingleOrDefault(item => item.Handle == monitor).Display?.Id
            ?? throw new WindowUnavailableException("The window's monitor is unavailable.");
    }

    public void MoveOnDisplay(WindowSnapshot snapshot, PixelRect target, DisplayInfo display, PixelRect viewport)
    {
        RequireIdentity(snapshot);
        var configuration = GetDisplayConfiguration();
        if (!configuration.Contains(display)) { throw new InvalidOperationException("The monitor configuration changed during placement."); }
        if (target.Width > viewport.Width || target.Height > viewport.Height)
        { throw ClipRejected(snapshot, "is too large to fit fully on this monitor"); }
        var physical = MonitorPlacement.PhysicalTarget(target, viewport, configuration);
        // Blank the old region before moving: its local coordinates would
        // otherwise travel across a neighbor until the new clip is installed.
        var frame = ReadPositionedFrame(snapshot);
        if (!NativeMethods.GetWindowRect((nint)snapshot.Identity.Handle, out var outer)) { throw NativeError("Could not read window bounds"); }
        SetRegion((nint)snapshot.Identity.Handle, MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0));
        var leftBorder = frame.Left - outer.Left;
        var topBorder = frame.Top - outer.Top;
        var rightBorder = outer.Right - frame.Right;
        var bottomBorder = outer.Bottom - frame.Bottom;
        var requestedOuter = new PixelRect(physical.Left - leftBorder, physical.Top - topBorder,
            physical.Width + leftBorder + rightBorder, physical.Height + topBorder + bottomBorder);
        if (!NativeMethods.SetWindowPos((nint)snapshot.Identity.Handle, 0, requestedOuter.Left, requestedOuter.Top,
            requestedOuter.Width, requestedOuter.Height, 0x0004 | 0x0010 | 0x0200))
        { throw NativeError("Window positioning failed"); }
        if (physical == target) { projectedFrames.Remove(snapshot.Identity); }
        else { projectedFrames[snapshot.Identity] = new(snapshot.RecoveryToken, target, physical, requestedOuter,
            !MonitorPlacement.Intersects(target, viewport)); }
        placingRegions.Add(snapshot.Identity.Handle);
        try { EnsurePresentation(snapshot, display, viewport); }
        finally { placingRegions.Remove(snapshot.Identity.Handle); }
    }

    public PixelRect ReadManagedFrame(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        var actual = ReadPositionedFrame(snapshot);
        if (projectedFrames.TryGetValue(snapshot.Identity, out var parked) && parked.Token == snapshot.RecoveryToken &&
            actual.Left == parked.Physical.Left && actual.Top == parked.Physical.Top)
        {
            // Preserve real size feedback. Do not mask Windows clamping or an
            // app moving out of the parking position: those must fail layout.
            return parked.Logical with { Width = actual.Width, Height = actual.Height };
        }
        return actual;
    }

    public PixelRect ReadPositionedFrame(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        return projectedFrames.TryGetValue(snapshot.Identity, out var parked) && parked.Token == snapshot.RecoveryToken
            ? MonitorPlacement.FrameFromOuter(ReadOuterFrame(snapshot.Identity.Handle), parked.Outer, parked.Physical)
            : ReadFrame(snapshot.Identity.Handle);
    }

    public void EnsurePresentation(WindowSnapshot snapshot, DisplayInfo display, PixelRect viewport)
    {
        RequireIdentity(snapshot);
        var handle = (nint)snapshot.Identity.Handle;
        if (!NativeMethods.GetWindowRect(handle, out var outer)) { throw NativeError("Could not read clipping bounds"); }
        var frame = ReadPositionedFrame(snapshot);
        if (projectedFrames.TryGetValue(snapshot.Identity, out var parked) && parked.Token == snapshot.RecoveryToken && parked.OutsideDesktop)
        {
            var outerBounds = Convert(outer);
            if (GetDisplayConfiguration().Any(item => MonitorPlacement.Intersects(outerBounds, item.Bounds)))
            { throw ClipRejected(snapshot, "could not keep its hidden window outside the connected screens"); }
            // No connected screen intersects this window, even if its app or
            // compositor ignores/replaces an empty region. Physical isolation
            // is the guarantee for parked columns; clipping is for visible ones.
            regionRepairs.Remove(snapshot.Identity.Handle);
            return;
        }
        if (GetDisplayConfiguration().Count > 1 && !MonitorPlacement.Contains(display.Bounds, Convert(outer)))
        { throw ClipRejected(snapshot, "could not keep its full window frame inside its monitor"); }
        var allowed = new PixelRect(Math.Max(display.Bounds.Left, viewport.Left - (frame.Left - outer.Left)),
            Math.Max(display.Bounds.Top, viewport.Top - (frame.Top - outer.Top)), 0, 0);
        var right = Math.Min(display.Bounds.Right, viewport.Right + (outer.Right - frame.Right));
        var bottom = Math.Min(display.Bounds.Bottom, viewport.Bottom + (outer.Bottom - frame.Bottom));
        var left = Math.Max(outer.Left, allowed.Left);
        var top = Math.Max(outer.Top, allowed.Top);
        right = Math.Min(outer.Right, right); bottom = Math.Min(outer.Bottom, bottom);
        var rtl = (NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & 0x00400000) != 0;
        var expected = right <= left || bottom <= top ? MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0)
            : MonitorNativeMethods.CreateRectRgn(rtl ? outer.Right - right : left - outer.Left, top - outer.Top,
                rtl ? outer.Right - left : right - outer.Left, bottom - outer.Top);
        if (expected == 0) { throw NativeError("Could not allocate a window clip"); }
        var current = MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (current == 0) { MonitorNativeMethods.DeleteObject(expected); throw NativeError("Could not inspect a window clip"); }
        try
        {
            var permitted = RegionBounds(expected);
            if (MonitorClip.Accepts(permitted, CurrentDrawingBounds(handle, current)))
            { regionRepairs.Remove(snapshot.Identity.Handle); return; }
            if (!placingRegions.Contains(snapshot.Identity.Handle))
            {
                var repairs = regionRepairs.GetValueOrDefault(snapshot.Identity.Handle) + 1;
                regionRepairs[snapshot.Identity.Handle] = repairs;
                if (repairs > 2) { throw ClipRejected(snapshot, "repeatedly replaced its monitor clip"); }
            }
            // SetWindowRgn takes ownership only on success. Keep a copy for
            // verification; never delete a region that Windows now owns.
            var data = RegionData(expected);
            SetRegion(handle, CreateRegion(data, 1));
            if (!MonitorClip.Accepts(permitted, CurrentDrawingBounds(handle, current)))
            { throw ClipRejected(snapshot, "could not keep its window inside the monitor clip"); }
        }
        finally { MonitorNativeMethods.DeleteObject(current); MonitorNativeMethods.DeleteObject(expected); }
    }

    private static PixelRect RegionBounds(nint region)
    {
        var type = MonitorNativeMethods.GetRgnBox(region, out var bounds);
        if (type == 0) { throw NativeError("Could not inspect monitor clipping"); }
        return type == 1 ? new(0, 0, 0, 0) : Convert(bounds);
    }

    private static PixelRect CurrentDrawingBounds(nint handle, nint region)
    {
        if (MonitorNativeMethods.GetWindowRgn(handle, region) != 0) { return RegionBounds(region); }
        // No explicit region means the whole outer window can draw. Accept
        // this only if it fits, never for a partially/fully off-viewport column.
        if (!NativeMethods.GetWindowRect(handle, out var outer)) { throw NativeError("Could not read clipping bounds"); }
        return new(0, 0, outer.Right - outer.Left, outer.Bottom - outer.Top);
    }

    private static InvalidOperationException ClipRejected(WindowSnapshot snapshot, string reason)
    {
        var application = "An application";
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(snapshot.Identity.ProcessId);
            application = process.ProcessName;
        }
        catch (ArgumentException) { }
        catch (System.ComponentModel.Win32Exception) { }
        return new($"{application} {reason}. Original placements will be restored. Try starting without this window.");
    }

    public PixelRect? ReadPresentationBounds(long window)
    {
        var handle = (nint)window;
        var region = MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw NativeError("Could not inspect the window region"); }
        try
        {
            if (MonitorNativeMethods.GetWindowRgn(handle, region) == 0) { return null; }
            var type = MonitorNativeMethods.GetRgnBox(region, out var local);
            if (type == 0) { throw NativeError("Could not read region bounds"); }
            if (type == 1) { return new(0, 0, 0, 0); }
            if (!NativeMethods.GetWindowRect(handle, out var outer)) { throw NativeError("Could not read window bounds"); }
            var rtl = (NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & 0x00400000) != 0;
            return new(rtl ? outer.Right - local.Right : outer.Left + local.Left, outer.Top + local.Top,
                local.Right - local.Left, local.Bottom - local.Top);
        }
        finally { MonitorNativeMethods.DeleteObject(region); }
    }

    public byte[]? ReadPresentationRegion(long window) => ReadWindowRegion((nint)window);

    public PixelRect ReadOuterFrame(long window)
    {
        if (!NativeMethods.GetWindowRect((nint)window, out var outer)) { throw NativeError("Could not read the outer frame"); }
        return Convert(outer);
    }

    public void SuspendPresentation(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        if (NativeMethods.IsIconic((nint)snapshot.Identity.Handle))
        { SetRegion((nint)snapshot.Identity.Handle, MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0)); }
        else { RestorePresentation(snapshot); }
    }

    public void RestorePresentation(WindowSnapshot snapshot)
    {
        RequireIdentity(snapshot);
        regionRepairs.Remove(snapshot.Identity.Handle);
        projectedFrames.Remove(snapshot.Identity);
        // Version 1 journals predate clipping and contain no original region.
        if (snapshot.OriginalDisplay is null && snapshot.OriginalRegion is null) { return; }
        var handle = (nint)snapshot.Identity.Handle;
        if (snapshot.OriginalRegion is null)
        {
            if (ReadWindowRegion(handle) is not null && MonitorNativeMethods.SetWindowRgn(handle, 0, true) == 0)
            { throw NativeError("Could not restore the original window region"); }
            if (ReadWindowRegion(handle) is not null) { throw new InvalidOperationException("The original window region did not restore; recovery was retained."); }
        }
        else
        {
            if (!NativeMethods.GetWindowRect(handle, out var outer)) { throw NativeError("Could not read restoration bounds"); }
            var original = snapshot.OriginalOuterFrame ?? snapshot.Placement.NormalBounds;
            var region = CreateRegion(snapshot.OriginalRegion, (outer.Right - outer.Left) / (double)original.Width,
                (outer.Bottom - outer.Top) / (double)original.Height);
            byte[] expected;
            try { expected = RegionData(region); }
            catch { MonitorNativeMethods.DeleteObject(region); throw; }
            SetRegion(handle, region);
            if (ReadWindowRegion(handle) is not { } actual || !actual.SequenceEqual(expected))
            { throw new InvalidOperationException("The original window region did not restore; recovery was retained."); }
        }
    }

    private static byte[]? ReadWindowRegion(nint handle)
    {
        var region = MonitorNativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw NativeError("Could not inspect the original window region"); }
        try { return MonitorNativeMethods.GetWindowRgn(handle, region) == 0 ? null : RegionData(region); }
        finally { MonitorNativeMethods.DeleteObject(region); }
    }
    private static byte[] RegionData(nint region)
    {
        var length = MonitorNativeMethods.GetRegionData(region, 0, null);
        if (length is < 32 or > 1048576) { throw new InvalidOperationException("The window region is unavailable or too complex to recover."); }
        var data = new byte[length];
        if (MonitorNativeMethods.GetRegionData(region, length, data) != length) { throw NativeError("Could not save the original window region"); }
        return data;
    }
    private static nint CreateRegion(byte[] data, double scale, double? scaleY = null)
    {
        if (data.Length is < 32 or > 1048576 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 32 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) > (data.Length - 32) / 16)
        { throw new InvalidOperationException("The saved window region is invalid; recovery was retained."); }
        var transform = new MonitorNativeMethods.Transform { M11 = (float)scale, M22 = (float)(scaleY ?? scale) };
        var region = MonitorNativeMethods.ExtCreateRegion(in transform, (uint)data.Length, data);
        return region != 0 ? region : throw NativeError("Could not recreate the saved window region");
    }
    private static void SetRegion(nint handle, nint region)
    {
        if (region == 0) { throw NativeError("Could not allocate a window region"); }
        if (MonitorNativeMethods.SetWindowRgn(handle, region, true) != 0) { return; }
        MonitorNativeMethods.DeleteObject(region);
        throw NativeError("Could not apply the window region");
    }
}
