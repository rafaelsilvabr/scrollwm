using System.Diagnostics;
using System.Text.Json;
using ScrollWM.Core.Management;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.Spike;

internal static class MonitorFixtureVerification
{
    public static async Task<int> RunAsync(NativeWindowSystem native, string directory, string reportPath)
    {
        var displays = native.GetDisplayConfiguration();
        var app = Path.Combine(AppContext.BaseDirectory, "ScrollWM.App.exe");
        if (!File.Exists(app)) { app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/ScrollWM.App/bin/Release/net10.0-windows/ScrollWM.App.exe")); }
        var start = new ProcessStartInfo(app) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--fixture"); start.ArgumentList.Add("5");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not open the disposable monitor fixtures.");
        var checks = new Dictionary<string, bool>();
        var store = new JsonRecoveryStore(directory);
        var session = new StripSession(native, store);
        WindowCandidate[] fixtures = [];
        Dictionary<WindowIdentity, PixelRect> originals = [];
        Dictionary<WindowIdentity, byte[]?> originalRegions = [];
        RestoreResult? pauseRestore = null;
        RestoreResult? crashRestore = null;
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                fixtures = native.EnumerateCandidates().Where(item => item.Identity.ProcessId == child.Id && NativeWindowSystem.IsTestFixture(item.Identity.Handle))
                    .OrderBy(item => item.Identity.Handle).ToArray();
                if (fixtures.Length == 5) { break; }
                await Task.Delay(100);
            }
            if (fixtures.Length != 5) { throw new InvalidOperationException("The disposable windows did not open."); }
            for (var index = 0; index < fixtures.Length; index++)
            {
                var display = displays[displays.Count > 1 && index >= 2 ? 1 : 0];
                NativeWindowSystem.PositionTestFixture(fixtures[index].Identity.Handle,
                    new(display.WorkArea.Left + display.Scale(50 + index * 15), display.WorkArea.Top + display.Scale(60), display.Scale(440), display.Scale(300)));
            }
            await Task.Delay(300);
            NativeWindowSystem.SetTestFixtureRegion(fixtures[0].Identity.Handle);
            await Task.Delay(150);
            originals = fixtures.ToDictionary(item => item.Identity, item => native.ReadFrame(item.Identity.Handle));
            originalRegions = fixtures.ToDictionary(item => item.Identity, item => native.ReadPresentationRegion(item.Identity.Handle));
            checks["CustomRegionPresentBeforeCapture"] = originalRegions[fixtures[0].Identity] is { Length: > 32 };
            await session.StartAsync(fixtures.Select(item => item.Identity));
            checks["AllFixturesCaptured"] = store.Load()!.Windows.Length == 5;
            checks["DisplayOwnership"] = session.Strips.Count == displays.Count &&
                session.AllColumns.Count == 5 && (displays.Count < 2 || session.Strips[displays[0].Id].Columns.Count == 2 && session.Strips[displays[1].Id].Columns.Count == 3);
            checks["InitialPlacementAndClipping"] = Matches();
            var sleepy = fixtures[^1].Identity;
            NativeWindowSystem.MinimizeTestFixture(sleepy.Handle, true);
            await Task.Delay(100);
            await session.ReconcileAsync();
            var minimizedBlank = native.ReadPresentationBounds(sleepy.Handle) is { Width: 0 };
            NativeWindowSystem.MinimizeTestFixture(sleepy.Handle, false);
            await Task.Delay(100);
            await session.ReconcileAsync();
            checks["MinimizeRestoreKeepsClipping"] = minimizedBlank && session.AllColumns.Count == 5 && Matches();
            await session.SetActiveDisplayAsync(displays[0].Id);
            var otherFrames = displays.Count > 1 ? session.Strips[displays[1].Id].Layout() : [];
            var otherOuterFrames = otherFrames.ToDictionary(item => item.Identity, item => native.ReadOuterFrame(item.Identity.Handle));
            var otherColumns = displays.Count > 1 ? session.Strips[displays[1].Id].Columns.ToArray() : [];
            await session.NavigateAsync(1);
            await session.CycleWidthAsync();
            var partial = session.Strips.SelectMany(pair => pair.Value.Layout().Where(target =>
                MonitorPlacement.Intersects(target.Bounds, pair.Value.Viewport) && !MonitorPlacement.Contains(pair.Value.Viewport, target.Bounds))
                .Select(target => (Display: pair.Key, Target: target))).ToArray();
            checks["EdgeWindowsStayVisibleBehindFullWindows"] = partial.Length > 0 && partial.All(edge =>
            {
                var captured = store.Load()!.Windows;
                var outer = native.ReadOuterFrame(edge.Target.Identity.Handle);
                var full = session.Strips[edge.Display].Layout().Where(target => MonitorPlacement.Contains(session.Strips[edge.Display].Viewport, target.Bounds));
                return MonitorPlacement.Contains(displays.Single(item => item.Id == edge.Display).Bounds, outer) &&
                    full.All(front => native.IsBehind(captured.Single(item => item.Identity == edge.Target.Identity), captured.Single(item => item.Identity == front.Identity)));
            });
            checks["NavigationAndWidthRemainClipped"] = Matches();
            checks["OtherDisplayUnchanged"] = displays.Count < 2 || otherFrames.SequenceEqual(session.Strips[displays[1].Id].Layout()) &&
                otherColumns.SequenceEqual(session.Strips[displays[1].Id].Columns) && otherFrames.All(item => native.ReadOuterFrame(item.Identity.Handle) == otherOuterFrames[item.Identity]);
            var edgeToReveal = partial[0];
            await session.SetActiveDisplayAsync(edgeToReveal.Display);
            var savedWidths = session.AllColumns.ToDictionary(item => item.Identity, item => item.Width);
            while (session.Strip!.Focused != edgeToReveal.Target.Identity)
            {
                var columns = session.Strip.Columns.ToList();
                var direction = columns.FindIndex(item => item.Identity == edgeToReveal.Target.Identity) > columns.FindIndex(item => item.Identity == session.Strip.Focused) ? 1 : -1;
                await session.NavigateAsync(direction);
            }
            checks["FocusRevealsEdgeWithoutChangingWidths"] = Matches() &&
                MonitorPlacement.Contains(session.Strip.Viewport, session.Strip.Layout().Single(item => item.Identity == edgeToReveal.Target.Identity).Bounds) &&
                session.AllColumns.All(item => item.Width == savedWidths[item.Identity]);
            if (displays.Count > 1)
            {
                var originalSnapshots = store.Load()!.Windows;
                var transfer = session.Strip!.Focused!;
                await session.TransferFocusedAsync(displays[1].Id);
                checks["TransferChangesOwnership"] = !session.Strips[displays[0].Id].Columns.Any(item => item.Identity == transfer) &&
                    session.Strips[displays[1].Id].Columns.Any(item => item.Identity == transfer) && Matches();
                checks["TransferKeepsOriginals"] = originalSnapshots.Zip(store.Load()!.Windows).All(pair =>
                    pair.First.Identity == pair.Second.Identity && pair.First.Placement == pair.Second.Placement && pair.First.VisibleFrame == pair.Second.VisibleFrame);
            }
            await session.PauseAsync();
            pauseRestore = session.LastRestore;
            await Task.Delay(250);
            checks["PauseRestoresAllRegionsAndFrames"] = session.LastRestore is { Restored: 5, Remaining: 0 } &&
                fixtures.All(item => native.ReadFrame(item.Identity.Handle) == originals[item.Identity] &&
                    ((native.ReadPresentationRegion(item.Identity.Handle) ?? []).SequenceEqual(originalRegions[item.Identity] ?? [])));
            await session.ResumeAsync();
            checks["ResumePreservesClipping"] = Matches();
            await session.PauseAsync();
            checks["FinalRecoveryEmpty"] = store.Load() is { Windows.Length: 0 };
            checks["FreshProcessCrashRecovery"] = await CrashRecoveryAsync();
        }
        finally
        {
            await session.PauseAsync();
            if (!child.HasExited)
            {
                foreach (var fixture in fixtures)
                { if (NativeWindowSystem.IsTestFixture(fixture.Identity.Handle)) { NativeWindowSystem.CloseTestFixture(fixture.Identity.Handle); } }
                if (!child.WaitForExit(3000)) { child.Kill(); child.WaitForExit(); }
            }
            var path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new { DisplayCount = displays.Count, TwoMonitorChecksExecuted = displays.Count >= 2,
                Checks = checks, PauseRestore = pauseRestore, CrashRestore = crashRestore, Cleanup = session.LastRestore }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json); Console.WriteLine(json);
        }
        return checks.Count >= 8 && checks.Values.All(value => value) ? 0 : 2;

        bool Matches() => session.Strips.All(pair => pair.Value.Layout().All(target =>
        {
            var display = displays.Single(item => item.Id == pair.Key);
            var physical = MonitorPlacement.PhysicalTarget(target.Bounds, pair.Value.Viewport, displays);
            var actual = native.ReadPositionedFrame(store.Load()!.Windows.Single(item => item.Identity == target.Identity));
            var outer = native.ReadOuterFrame(target.Identity.Handle);
            return actual == physical && (!MonitorPlacement.Intersects(target.Bounds, pair.Value.Viewport)
                ? displays.All(item => !MonitorPlacement.Intersects(outer, item.Bounds))
                : displays.Count == 1 || MonitorPlacement.Contains(display.Bounds, outer));
        }));

        async Task<bool> CrashRecoveryAsync()
        {
            var workerStart = WorkerStart("park-monitor-fixture-worker", child.Id, directory);
            using var worker = Process.Start(workerStart) ?? throw new InvalidOperationException("Could not open the recovery test worker.");
            try
            {
                var ready = worker.StandardOutput.ReadLineAsync();
                if (await Task.WhenAny(ready, Task.Delay(15000)) != ready || await ready != "PARKED")
                { throw new InvalidOperationException("The recovery test worker did not finish placement. " +
                    (worker.HasExited ? await worker.StandardError.ReadToEndAsync() : "Timed out.")); }
                worker.Kill(); worker.WaitForExit();
                using var restorer = Process.Start(WorkerStart("recover-monitor-fixture-worker", child.Id, directory))
                    ?? throw new InvalidOperationException("Could not open the fresh recovery process.");
                var exited = restorer.WaitForExitAsync();
                if (await Task.WhenAny(exited, Task.Delay(15000)) != exited)
                { restorer.Kill(); restorer.WaitForExit(); throw new TimeoutException("Fresh-process restoration timed out."); }
                await Task.Delay(250);
                crashRestore = JsonSerializer.Deserialize<RestoreResult>(await restorer.StandardOutput.ReadToEndAsync());
                return restorer.ExitCode == 0 && crashRestore is { Restored: 5, Remaining: 0 } && store.Load() is { Windows.Length: 0 } && fixtures.All(item =>
                    native.ReadFrame(item.Identity.Handle) == originals[item.Identity] &&
                    (native.ReadPresentationRegion(item.Identity.Handle) ?? []).SequenceEqual(originalRegions[item.Identity] ?? []));
            }
            finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
        }
    }

    private static ProcessStartInfo WorkerStart(string mode, int fixturePid, string directory)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ScrollWM.Spike.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(mode); start.ArgumentList.Add(fixturePid.ToString()); start.ArgumentList.Add(Path.GetFullPath(directory));
        return start;
    }

    public static void ParkWorker(NativeWindowSystem native, int fixturePid, string directory)
    {
        var fixtures = native.EnumerateCandidates().Where(item => item.Identity.ProcessId == fixturePid &&
            NativeWindowSystem.IsTestFixture(item.Identity.Handle)).Select(item => item.Identity).ToArray();
        if (fixtures.Length != 5) { throw new InvalidOperationException("The recovery worker requires five marked disposable windows."); }
        var session = new StripSession(native, new JsonRecoveryStore(directory));
        session.StartAsync(fixtures).GetAwaiter().GetResult();
        Console.WriteLine("PARKED"); Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    public static int RecoverWorker(NativeWindowSystem native, int fixturePid, string directory)
    {
        var recovery = new RecoverySession(new JsonRecoveryStore(directory), native);
        if (recovery.Pending is not { Windows.Length: 5 } pending || pending.Windows.Any(item =>
            item.Identity.ProcessId != fixturePid || !NativeWindowSystem.IsTestFixture(item.Identity.Handle)))
        { throw new InvalidOperationException("The recovery worker only accepts its marked disposable windows."); }
        var result = recovery.RestoreAll();
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Remaining == 0 ? 0 : 2;
    }
}
