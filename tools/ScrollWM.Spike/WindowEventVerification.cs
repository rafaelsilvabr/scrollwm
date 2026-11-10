using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ScrollWM.Core.Management;
using ScrollWM.Windows;

namespace ScrollWM.Spike;

internal static class WindowEventVerification
{
    public static int Run(NativeWindowSystem native, string reportPath)
    {
        var events = new ConcurrentQueue<DesktopWindowEvent>();
        var checks = new Dictionary<string, bool>();
        using var listener = new NativeWindowEvents(events.Enqueue);
        var appPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/ScrollWM.App/bin/Release/net10.0-windows/ScrollWM.App.exe"));
        var start = new ProcessStartInfo(appPath) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--fixture");
        start.ArgumentList.Add("1");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the disposable event fixture.");
        long handle = 0;
        try
        {
            WaitUntil(() =>
            {
                var candidate = native.EnumerateCandidates().FirstOrDefault(item =>
                    item.Identity.ProcessId == child.Id && NativeWindowSystem.IsTestFixture(item.Identity.Handle));
                handle = candidate?.Identity.Handle ?? 0;
                return handle != 0;
            });
            checks["CreatedOrShown"] = WaitUntil(() => events.Any(item => item.Handle == handle &&
                item.Kind is DesktopWindowEventKind.Created or DesktopWindowEventKind.Shown));
            NativeWindowSystem.MinimizeTestFixture(handle, true);
            checks["Minimized"] = WaitUntil(() => events.Any(item => item.Handle == handle && item.Kind == DesktopWindowEventKind.Minimized));
            NativeWindowSystem.MinimizeTestFixture(handle, false);
            checks["Restored"] = WaitUntil(() => events.Any(item => item.Handle == handle && item.Kind == DesktopWindowEventKind.Restored));
            NativeWindowSystem.CloseTestFixture(handle);
            checks["Destroyed"] = WaitUntil(() => events.Any(item => item.Handle == handle && item.Kind == DesktopWindowEventKind.Destroyed));
            checks["CallbackHealthy"] = listener.Failure is null;
        }
        finally
        {
            if (!child.HasExited)
            {
                if (handle != 0 && NativeWindowSystem.IsTestFixture(handle)) { NativeWindowSystem.CloseTestFixture(handle); }
                if (!child.WaitForExit(3000)) { child.Kill(); child.WaitForExit(); }
            }
            listener.Dispose();
            checks["ListenerStopped"] = listener.Failure is null;
            var path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new { Checks = checks, Observed = events.Where(item => item.Handle == handle)
                .GroupBy(item => item.Kind.ToString()).ToDictionary(group => group.Key, group => group.Count()) },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Console.WriteLine(json);
        }
        return checks.Count == 6 && checks.Values.All(item => item) ? 0 : 2;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (condition()) { return true; }
            Thread.Sleep(50);
        }
        return false;
    }
}
