using System.Text.Json;
using ScrollWM.Core.Layout;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.Spike;

// Explicit full-identity selection only. Always journal and restore ordinary apps.
internal static class SelectedMonitorProbe
{
    public static async Task<int> RunAsync(NativeWindowSystem native, string selection, string directory, string reportPath)
    {
        var chosen = SelectedWindowProbe.Select(native, selection, includeMaximized: false);
        var session = new RecoverySession(new JsonRecoveryStore(directory), native);
        var snapshots = session.Begin(chosen.Select(item => item.Identity));
        var results = new List<object>();
        string? failure = null;
        RestoreResult restore;
        try
        {
            foreach (var snapshot in snapshots)
            foreach (var display in native.GetDisplayConfiguration())
            foreach (var probe in Enumerable.Range(0, 6))
            {
                var viewport = new ScrollingStrip(display.WorkArea, display.Scale(12),
                    MonitorPlacement.Margin(display, native.GetDisplayConfiguration().Count)).Viewport;
                var position = probe == 5 ? "FullWidth" : ((ProbePosition)probe).ToString();
                var target = probe == 5 ? viewport : ProbeLayout.Target(viewport, viewport.Width / 2, (ProbePosition)probe);
                var application = chosen.Single(item => item.Identity == snapshot.Identity).ProcessName;
                try
                {
                    native.MoveOnDisplay(snapshot, target, display, viewport);
                    await Task.Delay(250);
                    native.EnsurePresentation(snapshot, display, viewport);
                    await Task.Delay(100);
                    native.EnsurePresentation(snapshot, display, viewport);
                    var physical = MonitorPlacement.PhysicalTarget(target, viewport, native.GetDisplayConfiguration());
                    if (native.ReadPositionedFrame(snapshot) != physical)
                    { throw new InvalidOperationException("The selected window did not reach its physical placement."); }
                    var outer = native.ReadOuterFrame(snapshot.Identity.Handle);
                    var isolated = !MonitorPlacement.Intersects(target, viewport)
                        ? native.GetDisplayConfiguration().All(item => !MonitorPlacement.Intersects(outer, item.Bounds))
                        : native.GetDisplayConfiguration().Count == 1 || MonitorPlacement.Contains(display.Bounds, outer);
                    if (!isolated) { throw new InvalidOperationException("The selected window's outer frame crossed a monitor boundary."); }
                    results.Add(new { Application = application, Display = display.Primary ? "Primary" : "Secondary",
                        Position = position, Target = target, Frame = native.ReadPositionedFrame(snapshot),
                        Outer = outer, PhysicallyIsolated = isolated, Clip = native.ReadPresentationBounds(snapshot.Identity.Handle), Passed = true });
                }
                catch (Exception error)
                {
                    results.Add(new { Application = application, Display = display.Primary ? "Primary" : "Secondary",
                        Position = position, Target = target, Frame = native.ReadFrame(snapshot.Identity.Handle),
                        Clip = native.ReadPresentationBounds(snapshot.Identity.Handle), Passed = false, Error = error.Message });
                    throw;
                }
            }
        }
        catch (Exception error) { failure = error.Message; }
        finally { restore = session.RestoreAll(); }
        await Task.Delay(250);
        var restored = snapshots.All(item => native.CheckIdentity(item) == IdentityStatus.ClosedOrReused ||
            native.ReadFrame(item.Identity.Handle) == item.VisibleFrame &&
            (native.ReadPresentationRegion(item.Identity.Handle) ?? []).SequenceEqual(item.OriginalRegion ?? []));
        var report = new { Probes = results, Failure = failure, Restore = restore, OriginalsMatch = restored };
        var path = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        Console.WriteLine(json);
        return failure is null && restore.Remaining == 0 && restored ? 0 : 2;
    }
}
