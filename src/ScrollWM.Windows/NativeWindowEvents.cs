using System.ComponentModel;
using System.Runtime.InteropServices;
using ScrollWM.Core.Management;
using ScrollWM.Windows.Native;

namespace ScrollWM.Windows;

// Hooks and unhooks run on the same dedicated message-pump thread. Callbacks
// only publish metadata; clients must queue actual desktop work elsewhere.
public sealed class NativeWindowEvents : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new(false);
    private readonly Action<DesktopWindowEvent> publish;
    private Exception? failure;
    private uint nativeThreadId;
    private int disposed;

    public Exception? Failure => Volatile.Read(ref failure);

    public NativeWindowEvents(Action<DesktopWindowEvent> publish)
    {
        this.publish = publish;
        thread = new Thread(Run) { IsBackground = true, Name = "ScrollWM window events" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            Dispose();
            throw new TimeoutException("The native window event listener did not start.");
        }

        if (Failure is { } error)
        {
            Dispose();
            throw new InvalidOperationException("The native window event listener could not start.", error);
        }
    }

    private void Run()
    {
        var hooks = new List<nint>();
        NativeMethods.WinEventCallback callback = OnEvent;
        var pinnedCallback = GCHandle.Alloc(callback);
        try
        {
            nativeThreadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, 0, 0, 0, 0); // Create this thread's queue before exposing it.
            foreach (var range in new (uint First, uint Last)[]
            {
                (3, 3), (0xA, 0xB), (0x16, 0x17), (0x8000, 0x8003), (0x800A, 0x800B), (0x8017, 0x8018),
            })
            {
                var hook = NativeMethods.SetWinEventHook(range.First, range.Last, 0, callback, 0, 0, 2);
                if (hook == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Window event hook registration failed."); }
                hooks.Add(hook);
            }

            ready.Set();
            while (Volatile.Read(ref disposed) == 0)
            {
                var result = NativeMethods.GetMessage(out var message, 0, 0, 0);
                if (result == 0) { break; }
                if (result < 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Window event message loop failed."); }
                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception exception) { Volatile.Write(ref failure, exception); }
        finally
        {
            foreach (var hook in hooks) { NativeMethods.UnhookWinEvent(hook); }
            pinnedCallback.Free();
            ready.Set();
        }
    }

    private void OnEvent(nint hook, uint eventType, nint handle, int objectId, int childId, uint sourceThread, uint timestamp)
    {
        if (Volatile.Read(ref disposed) != 0 || handle == 0 ||
            eventType >= 0x8000 && (objectId != 0 || childId != 0)) { return; }
        var kind = Map(eventType);
        if (kind is null) { return; }
        try { publish(new(handle.ToInt64(), kind.Value)); }
        catch (Exception exception) { Volatile.Write(ref failure, exception); }
    }

    internal static DesktopWindowEventKind? Map(uint eventType) => eventType switch
    {
        3 => DesktopWindowEventKind.Foreground,
        0xA => DesktopWindowEventKind.MoveSizeStarted,
        0xB => DesktopWindowEventKind.MoveSizeEnded,
        0x16 => DesktopWindowEventKind.Minimized,
        0x17 => DesktopWindowEventKind.Restored,
        0x8000 => DesktopWindowEventKind.Created,
        0x8001 => DesktopWindowEventKind.Destroyed,
        0x8002 => DesktopWindowEventKind.Shown,
        0x8003 => DesktopWindowEventKind.Hidden,
        0x800A or 0x8017 or 0x8018 => DesktopWindowEventKind.StateChanged,
        0x800B => DesktopWindowEventKind.LocationChanged,
        _ => null,
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        if (nativeThreadId != 0) { NativeMethods.PostThreadMessage(nativeThreadId, 0x12, 0, 0); }
        if (Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The native event listener did not stop.");
        }
        if (Thread.CurrentThread != thread) { ready.Dispose(); }
    }
}
