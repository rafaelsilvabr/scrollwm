using System.Text.Json;
using ScrollWM.Core.Management;
using ScrollWM.Core.Windows;
using ScrollWM.Core.Recovery;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.Spike;

internal static class StripFixtureVerification
{
    public static async Task<int> RunAsync(NativeWindowSystem native, int pid, string directory, string reportPath)
    {
        native.RequireSingleDisplay();
        var fixtures = native.EnumerateCandidates().Where(item => item.Identity.ProcessId == pid &&
            NativeWindowSystem.IsTestFixture(item.Identity.Handle)).OrderBy(item => item.Identity.Handle).ToArray();
        if (fixtures.Length != 5) { throw new InvalidOperationException("Start --fixture 5 for the strip verification."); }
        var baseline = fixtures.ToDictionary(item => item.Identity, item => item.Frame);
        var store = new JsonRecoveryStore(directory);
        var session = new StripSession(native, store);
        var checks = new Dictionary<string, bool>();
        RestoreResult? finalRestore = null;
        try
        {
            await session.StartAsync(fixtures.Take(4).Select(item => item.Identity));
            checks["FourWindowLayout"] = MatchesLayout();
            var originalWidths = session.Strip!.Columns.ToDictionary(item => item.Identity, item => item.Width);
            await session.AddAsync([fixtures[4].Identity]);
            checks["FifthWindowPreservesWidths"] = session.Strip.Columns.Where(item => originalWidths.ContainsKey(item.Identity))
                .All(item => originalWidths[item.Identity] == item.Width) && MatchesLayout();
            await session.NavigateAsync(-1);
            await Task.Delay(100);
            var focused = session.Strip.Focused;
            var focusedFrame = session.Strip.Layout().Single(item => item.Identity == focused).Bounds;
            checks["NavigationRevealsFocus"] = focusedFrame.Left >= session.Strip.Viewport.Left &&
                focusedFrame.Right <= session.Strip.Viewport.Right &&
                (native.FocusedHandle == focused!.Handle || session.Status.Contains("Windows declined focus.", StringComparison.Ordinal));
            await session.ReorderAsync(-1);
            checks["ReorderRetainsFocus"] = session.Strip.Focused == focused && MatchesLayout();
            await session.CycleWidthAsync();
            checks["CycleWidthLayout"] = MatchesLayout();
            var columns = session.Strip.Columns.ToArray();
            await session.PauseAsync();
            await Task.Delay(200);
            checks["PauseRestoresAllFive"] = session.LastRestore!.Restored == 5 && session.LastRestore.Remaining == 0 &&
                fixtures.All(item => native.ReadFrame(item.Identity.Handle) == baseline[item.Identity]);
            await session.ResumeAsync();
            checks["ResumePreservesOrderAndWidths"] = columns.SequenceEqual(session.Strip.Columns) && MatchesLayout();
            NativeWindowSystem.CloseTestFixture(fixtures[4].Identity.Handle);
            await Task.Delay(250);
            await session.ReconcileAsync();
            checks["ClosedWindowRemoved"] = session.Strip.Columns.Count == 4 && MatchesLayout();
            await session.PauseAsync();
            await Task.Delay(200);
            checks["FinalRestore"] = session.LastRestore!.Restored == 4 && session.LastRestore.ClosedOrReused == 1 &&
                session.LastRestore.Remaining == 0 && fixtures.Take(4).All(item => native.ReadFrame(item.Identity.Handle) == baseline[item.Identity]);
            finalRestore = session.LastRestore;
        }
        finally
        {
            await session.PauseAsync();
            var path = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new { Checks = checks, Recovery = finalRestore, Cleanup = session.LastRestore },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Console.WriteLine(json);
        }

        return checks.Values.All(item => item) ? 0 : 2;
        bool MatchesLayout() => session.Strip!.Layout().All(item => native.ReadPositionedFrame(store.Load()!.Windows.Single(saved => saved.Identity == item.Identity)) ==
            MonitorPlacement.PhysicalTarget(item.Bounds, session.Strip.Viewport, native.GetDisplayConfiguration()));
    }
}
