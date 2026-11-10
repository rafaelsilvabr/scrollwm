using ScrollWM.Core.Windows;
using ScrollWM.Windows.Native;

namespace ScrollWM.Windows;

public sealed partial class NativeWindowSystem
{
    public void PlaceBehind(IReadOnlyList<WindowSnapshot> edges, IReadOnlyList<WindowSnapshot> visible)
    {
        if (edges.Count == 0) { return; }
        if (visible.Count == 0) { throw new InvalidOperationException("An edge window needs a visible window in front of it."); }
        foreach (var snapshot in edges.Concat(visible)) { RequireIdentity(snapshot); }
        var anchor = visible[0];
        foreach (var candidate in visible.Skip(1))
        { if (IsBehind(candidate, anchor)) { anchor = candidate; } }
        // Inserting across topmost groups changes an application's topmost
        // state. Refuse that case rather than change uncaptured presentation.
        var topmost = (NativeMethods.GetWindowLongPtr((nint)anchor.Identity.Handle, -20).ToInt64() & 8) != 0;
        foreach (var edge in edges)
        {
            var edgeTopmost = (NativeMethods.GetWindowLongPtr((nint)edge.Identity.Handle, -20).ToInt64() & 8) != 0;
            if (edgeTopmost != topmost) { throw ClipRejected(edge, "has an always-on-top setting that prevents overlapping edge windows"); }
        }
        foreach (var edge in edges)
        {
            if (visible.All(front => IsBehind(edge, front))) { continue; }
            RequireIdentity(edge); RequireIdentity(anchor);
            // Keep size, position, focus, and owner ordering. Never use TOP,
            // TOPMOST, or BOTTOM: only lower the edge below a managed anchor.
            if (!NativeMethods.SetWindowPos((nint)edge.Identity.Handle, (nint)anchor.Identity.Handle, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010 | 0x0200))
            { throw NativeError("Could not place the edge window behind its neighbors"); }
            if (visible.Any(front => !IsBehind(edge, front)))
            { throw ClipRejected(edge, "could not keep its edge window behind the visible windows"); }
        }
    }

    public bool IsBehind(WindowSnapshot back, WindowSnapshot front)
    {
        RequireIdentity(back); RequireIdentity(front);
        var current = (nint)back.Identity.Handle;
        for (var count = 0; count < 4096; count++)
        {
            current = NativeMethods.GetWindow(current, 3); // GW_HWNDPREV
            if (current == 0) { return false; }
            if (current == (nint)front.Identity.Handle) { return true; }
        }
        throw new InvalidOperationException("Window ordering did not settle. Original placements will be restored.");
    }
}
