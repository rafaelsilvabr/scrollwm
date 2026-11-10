using System.Text.Json;
using ScrollWM.Core.Layout;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.Spike;

internal static class SelectedWindowProbe
{
    public static int Run(NativeWindowSystem native, string selectionPath, string directory, string reportPath,
        bool includeMaximized = false)
    {
        native.RequireSingleDisplay();
        var chosen = Select(native, selectionPath, includeMaximized);
        var session = new RecoverySession(new JsonRecoveryStore(directory), new ProbeWindows(native, includeMaximized));
        var snapshots = session.Begin(chosen.Select(item => item.Identity));
        var results = new List<object>();
        RestoreResult? restore = null;
        string? failure = null;
        var workArea = native.GetDisplays().WorkArea;
        try
        {
            foreach (var snapshot in snapshots)
            {
                native.PrepareForProbe(snapshot);
                Thread.Sleep(250);
                var application = chosen.Single(item => item.Identity == snapshot.Identity).ProcessName;
                foreach (var position in Enum.GetValues<ProbePosition>())
                {
                    Measure(application, snapshot, position.ToString(), ProbeLayout.Target(workArea,
                        ColumnWidths.ResolvePixels(ColumnWidth.Half, workArea.Width), position));
                }

                foreach (var preset in Enum.GetValues<ColumnWidth>())
                {
                    Measure(application, snapshot, $"Width:{preset}", ProbeLayout.Target(workArea,
                        ColumnWidths.ResolvePixels(preset, workArea.Width), ProbePosition.Center));
                }

                Measure(application, snapshot, "MinimumWidth", ProbeLayout.Target(workArea, 40, ProbePosition.Center));
                native.Move(snapshot, ProbeLayout.Target(workArea, snapshot.VisibleFrame.Width, ProbePosition.Center));
                Thread.Sleep(200);
                var accepted = native.Focus(snapshot);
                Thread.Sleep(100);
                results.Add(new { Application = application, snapshot.Identity.Handle, Probe = "RevealFocus",
                    Accepted = accepted, ForegroundMatches = native.FocusedHandle == snapshot.Identity.Handle,
                    Observed = native.ReadFrame(snapshot.Identity.Handle) });
            }
        }
        catch (Exception exception)
        {
            failure = exception.Message;
        }
        finally
        {
            restore = session.RestoreAll();
            Thread.Sleep(250);
            var frames = snapshots.Select(snapshot =>
            {
                PixelRect? actual = null;
                try
                {
                    actual = native.ReadFrame(snapshot.Identity.Handle);
                }
                catch (System.ComponentModel.Win32Exception) { }
                return new { snapshot.Identity.Handle, Original = snapshot.VisibleFrame, Actual = actual,
                    Exact = actual == snapshot.VisibleFrame };
            }).ToArray();
            var report = new { OperatingSystem = Environment.OSVersion.VersionString, WorkArea = workArea,
                Applications = chosen.Select(item => new { item.ProcessName, item.Identity.Handle, item.Dpi,
                    OriginalShowCommand = item.Placement.ShowCommand }),
                Probes = results, Failure = failure, Restore = restore, RestoredFrames = frames };
            var absolutePath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(absolutePath, json);
            Console.WriteLine(json);
        }

        return failure is null && restore.Remaining == 0 ? 0 : 2;

        void Measure(string application, WindowSnapshot snapshot, string name, PixelRect target)
        {
            native.Move(snapshot, target);
            Thread.Sleep(250);
            var actual = native.ReadFrame(snapshot.Identity.Handle);
            results.Add(new { Application = application, snapshot.Identity.Handle, Probe = name,
                Requested = target, Observed = actual, Exact = target == actual });
        }
    }

    public static void Park(NativeWindowSystem native, string selectionPath, string directory)
    {
        native.RequireSingleDisplay();
        var selected = Select(native, selectionPath, includeMaximized: false);
        var session = new RecoverySession(new JsonRecoveryStore(directory), native);
        var snapshots = session.Begin(selected.Select(item => item.Identity));
        try
        {
            foreach (var snapshot in snapshots)
            {
                native.Move(snapshot, ProbeLayout.Target(native.GetDisplays().WorkArea,
                    snapshot.VisibleFrame.Width, ProbePosition.RightFull));
                Thread.Sleep(250);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    snapshot.Identity.Handle, Original = snapshot.VisibleFrame,
                    Parked = native.ReadFrame(snapshot.Identity.Handle),
                }));
            }
        }
        catch
        {
            session.RestoreAll();
            throw;
        }

        Console.WriteLine("PARKED: selected windows are journaled. Terminate only this worker, then run recover with the same directory.");
        Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    internal static WindowInspection[] Select(NativeWindowSystem native, string selectionPath, bool includeMaximized)
    {
        var selected = JsonSerializer.Deserialize<WindowIdentity[]>(File.ReadAllBytes(selectionPath))
            ?? throw new InvalidDataException("Select windows using identity records from the current inventory.");
        if (selected.Length == 0 || selected.Any(item => item is null) ||
            selected.Select(item => item.Handle).Distinct().Count() != selected.Length)
        {
            throw new InvalidDataException("The selection must contain distinct window identities.");
        }

        var candidates = native.InspectApplications()
            .Where(item => item.ExclusionReason is null || includeMaximized && item.ExclusionReason == "Maximized").ToArray();
        return selected.Select(identity => candidates.SingleOrDefault(item => item.Identity == identity)
            ?? throw new InvalidOperationException("A selected window changed or is no longer eligible. Refresh the inventory.")).ToArray();
    }

    private sealed class ProbeWindows(NativeWindowSystem native, bool includeMaximized) : IRestorableWindows
    {
        public WindowSnapshot Capture(WindowIdentity identity) =>
            includeMaximized ? native.CaptureForProbe(identity) : native.Capture(identity);
        public IdentityStatus CheckIdentity(WindowSnapshot snapshot) => native.CheckIdentity(snapshot);
        public void Restore(WindowSnapshot snapshot) => native.Restore(snapshot);
        public void ReleaseToken(WindowSnapshot snapshot) => native.ReleaseToken(snapshot);
    }
}
