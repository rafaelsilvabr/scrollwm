using System.Text.Json;
using ScrollWM.Core.Layout;
using ScrollWM.Core.Recovery;
using ScrollWM.Core.Windows;
using ScrollWM.Windows;
using ScrollWM.Windows.Recovery;

namespace ScrollWM.Spike;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var native = new NativeWindowSystem();
            if (args is ["list"])
            {
                Print(new { Displays = native.GetDisplays(), Monitors = native.GetDisplayConfiguration(), Windows = native.EnumerateCandidates() });
                return 0;
            }

            if (args is ["inspect"])
            {
                Print(new { Displays = native.GetDisplays(), Monitors = native.GetDisplayConfiguration(), ForegroundHandle = native.FocusedHandle, Windows = native.InspectApplications() });
                return 0;
            }

            // These fixture-only child modes run under the verification
            // parent's session lock. They never select ordinary app windows.
            if (args is ["park-monitor-fixture-worker", var workerPidText, var workerDirectory] && int.TryParse(workerPidText, out var workerFixturePid))
            {
                MonitorFixtureVerification.ParkWorker(native, workerFixturePid, workerDirectory);
                return 0;
            }
            if (args is ["recover-monitor-fixture-worker", var recoverPidText, var recoverWorkerDirectory] && int.TryParse(recoverPidText, out var recoverFixturePid))
            {
                return MonitorFixtureVerification.RecoverWorker(native, recoverFixturePid, recoverWorkerDirectory);
            }
            using var sessionLock = new SessionLock();
            if (args is ["verify-monitor-selected", var monitorSelection, var selectedMonitorDirectory, var selectedMonitorReport])
            {
                return SelectedMonitorProbe.RunAsync(native, monitorSelection, selectedMonitorDirectory, selectedMonitorReport).GetAwaiter().GetResult();
            }
            if (args is ["verify-monitors", var monitorDirectory, var monitorReport])
            {
                return MonitorFixtureVerification.RunAsync(native, monitorDirectory, monitorReport).GetAwaiter().GetResult();
            }
            if (args is ["verify-window-events", var eventReport])
            {
                return WindowEventVerification.Run(native, eventReport);
            }
            if (args is ["verify-strip-fixtures", var stripPidText, var stripDirectory, var stripReport] && int.TryParse(stripPidText, out var stripPid))
            {
                return StripFixtureVerification.RunAsync(native, stripPid, stripDirectory, stripReport).GetAwaiter().GetResult();
            }

            if (args is ["verify-selected", var selection, var dataDirectory, var selectedReport])
            {
                return SelectedWindowProbe.Run(native, selection, dataDirectory, selectedReport);
            }

            if (args is ["verify-selected", var maximizedSelection, var maximizedDirectory, var maximizedReport, "--include-maximized"])
            {
                return SelectedWindowProbe.Run(native, maximizedSelection, maximizedDirectory, maximizedReport, includeMaximized: true);
            }

            if (args is ["park-selected", var parkSelection, var selectedDirectory])
            {
                SelectedWindowProbe.Park(native, parkSelection, selectedDirectory);
                return 0;
            }

            if (args is ["recover", var recoveryDirectory])
            {
                var session = new RecoverySession(new JsonRecoveryStore(recoveryDirectory), native);
                var originals = session.Pending?.Windows ?? [];
                var result = session.RestoreAll();
                Thread.Sleep(250);
                var frames = originals.Where(item => result.Remaining == 0)
                    .Select(item => new { item.Identity.Handle, Original = item.VisibleFrame, Actual = TryReadFrame(native, item) }).ToArray();
                Print(new { Restore = result, Frames = frames });
                return result.Remaining == 0 ? 0 : 2;
            }

            if (args is ["capture-fixtures-and-wait", var capturePidText, var captureDirectory] && int.TryParse(capturePidText, out var capturePid))
            {
                var session = new RecoverySession(new JsonRecoveryStore(captureDirectory), native);
                session.Begin(Fixtures(native, capturePid).Select(item => item.Identity));
                Console.WriteLine("CAPTURED: no windows moved. Change the disposable fixtures, terminate this worker, then run recover.");
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            if (args is ["verify-fixtures", var pidText, var directory, var reportPath] && int.TryParse(pidText, out var pid))
            {
                native.RequireSingleDisplay();
                var candidates = Fixtures(native, pid);
                var session = new RecoverySession(new JsonRecoveryStore(directory), native);
                var snapshots = session.Begin(candidates.Select(item => item.Identity));
                var probes = new List<object>();
                RestoreResult? result = null;
                var area = native.GetDisplays().WorkArea;
                try
                {
                    foreach (var snapshot in snapshots)
                    {
                        foreach (var position in Enum.GetValues<ProbePosition>())
                        {
                            var target = ProbeLayout.Target(area, ColumnWidths.ResolvePixels(ColumnWidth.Half, area.Width), position);
                            native.Move(snapshot, target);
                            Thread.Sleep(250);
                            var actual = native.ReadFrame(snapshot.Identity.Handle);
                            probes.Add(new { snapshot.Identity.Handle, snapshot.Dpi, Position = position.ToString(), Requested = target,
                                Observed = actual, Exact = target == actual });
                        }

                        // Deliberately undersized request: record app-enforced minimum width.
                        var narrow = ProbeLayout.Target(area, 40, ProbePosition.Center);
                        native.Move(snapshot, narrow);
                        Thread.Sleep(250);
                        probes.Add(new { snapshot.Identity.Handle, Position = "MinimumWidth", Requested = narrow,
                            Observed = native.ReadFrame(snapshot.Identity.Handle) });
                        native.Move(snapshot, ProbeLayout.Target(area, snapshot.VisibleFrame.Width, ProbePosition.Center));
                        var focusAccepted = native.Focus(snapshot);
                        probes.Add(new { snapshot.Identity.Handle, Position = "Reveal", FocusAccepted = focusAccepted });
                    }
                }
                finally
                {
                    result = session.RestoreAll();
                    var report = new { OperatingSystem = Environment.OSVersion.VersionString, Display = native.GetDisplays(),
                        Captured = snapshots.Length, Probes = probes, Restore = result };
                    var absoluteReport = Path.GetFullPath(reportPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(absoluteReport)!);
                    File.WriteAllText(absoluteReport, JsonSerializer.Serialize(report, Json));
                    Print(report);
                }

                return result.Remaining == 0 ? 0 : 2;
            }

            if (args is ["park-fixtures", var parkPidText, var parkDirectory] && int.TryParse(parkPidText, out var parkPid))
            {
                native.RequireSingleDisplay();
                var session = new RecoverySession(new JsonRecoveryStore(parkDirectory), native);
                var snapshots = session.Begin(Fixtures(native, parkPid).Select(item => item.Identity));
                try
                {
                    foreach (var snapshot in snapshots)
                    {
                        native.Move(snapshot, ProbeLayout.Target(native.GetDisplays().WorkArea,
                            snapshot.VisibleFrame.Width, ProbePosition.RightFull));
                    }
                }
                catch
                {
                    session.RestoreAll();
                    throw;
                }

                Console.WriteLine("PARKED: fixture positions are journaled. Terminate this test worker, then run recover with the same data directory.");
                Console.Out.Flush();
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            Console.Error.WriteLine("Usage: ScrollWM.Spike list | inspect | verify-monitors <data-dir> <report.json> | verify-monitor-selected <identities.json> <data-dir> <report.json> | verify-window-events <report.json> | verify-selected <identities.json> <data-dir> <report.json> [--include-maximized] | park-selected <identities.json> <data-dir> | verify-fixtures <fixture-pid> <data-dir> <report.json> | verify-strip-fixtures <fixture-pid> <data-dir> <report.json> | park-fixtures <fixture-pid> <data-dir> | capture-fixtures-and-wait <fixture-pid> <data-dir> | recover <data-dir>");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static WindowCandidate[] Fixtures(NativeWindowSystem native, int processId)
    {
        var candidates = native.EnumerateCandidates()
            .Where(item => item.Identity.ProcessId == processId && NativeWindowSystem.IsTestFixture(item.Identity.Handle)).ToArray();
        if (candidates.Length != 3)
        {
            throw new InvalidOperationException("Expected exactly three marked fixture windows. Run ScrollWM.App --fixture first.");
        }

        return candidates;
    }

    private static PixelRect? TryReadFrame(NativeWindowSystem native, WindowSnapshot snapshot)
    {
        try
        {
            return native.ReadFrame(snapshot.Identity.Handle);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
