using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScrollWM.Windows;

public enum StripShortcut
{
    Previous = 0x5350,
    Next,
    MovePrevious,
    MoveNext,
    CycleWidth,
    PauseResume,
    TransferNext,
}

// Only held for an active/paused preview session. Registration is all-or-none.
public sealed class StripShortcuts : IDisposable
{
    private readonly nint owner;
    private readonly List<int> registered = [];

    public StripShortcuts(nint owner)
    {
        this.owner = owner;
        try
        {
            Add(StripShortcut.Previous, 0x4003, 0x25, "Ctrl+Alt+Left");
            Add(StripShortcut.Next, 0x4003, 0x27, "Ctrl+Alt+Right");
            Add(StripShortcut.MovePrevious, 0x4007, 0x25, "Ctrl+Alt+Shift+Left");
            Add(StripShortcut.MoveNext, 0x4007, 0x27, "Ctrl+Alt+Shift+Right");
            Add(StripShortcut.CycleWidth, 0x4003, 0x57, "Ctrl+Alt+W");
            Add(StripShortcut.PauseResume, 0x4003, 0x50, "Ctrl+Alt+P");
            Add(StripShortcut.TransferNext, 0x4003, 0x4D, "Ctrl+Alt+M");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Add(StripShortcut command, uint modifiers, uint key, string label)
    {
        if (!RegisterHotKey(owner, (int)command, modifiers, key))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Shortcut {label} is unavailable. The strip was not started.");
        }

        registered.Add((int)command);
    }

    public void Dispose()
    {
        foreach (var id in registered)
        {
            UnregisterHotKey(owner, id);
        }

        registered.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint owner, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint owner, int id);
}
