using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScrollWM.Windows;

public sealed class EmergencyShortcut : IDisposable
{
    public const int Message = 0x0312;
    public const int Id = 0x5343;
    private readonly nint owner;

    public EmergencyShortcut(nint owner)
    {
        this.owner = owner;
        // MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_ESCAPE.
        if (!RegisterHotKey(owner, Id, 0x4007, 0x1B))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Emergency restore shortcut is unavailable. Capture is disabled; existing recovery can still be restored.");
        }
    }

    public void Dispose() => UnregisterHotKey(owner, Id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint owner, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint owner, int id);
}
