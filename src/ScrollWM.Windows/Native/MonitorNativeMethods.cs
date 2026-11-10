using System.Runtime.InteropServices;

namespace ScrollWM.Windows.Native;

internal static partial class MonitorNativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public uint Size;
        public NativeMethods.Rect Bounds;
        public NativeMethods.Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Transform
    {
        public float M11, M12, M21, M22, Dx, Dy;
    }
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx information);
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice information, uint flags);
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("shcore.dll")]
    internal static extern int GetScaleFactorForMonitor(nint monitor, out int factor);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")]
    internal static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")]
    internal static extern uint GetRegionData(nint region, uint size, [Out] byte[]? buffer);
    [DllImport("gdi32.dll")]
    internal static extern nint ExtCreateRegion(in Transform transform, uint count, byte[] data);
    [DllImport("gdi32.dll")]
    internal static extern int GetRgnBox(nint region, out NativeMethods.Rect bounds);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EqualRgn(nint first, nint second);
}
