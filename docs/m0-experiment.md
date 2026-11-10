# M0 native experiment

Status: single-display fixtures plus the open Notepad/Chrome windows verified at 100% scale; the remaining application/scaling feasibility gate is still open. This workbench exercises real Windows APIs; it is separate from the eventual everyday scrolling-strip UI.

## Run the workbench

Build as described in the README, then run on Windows 11 x64:

```powershell
.\run.ps1
```

For a workspace-local recovery directory:

```powershell
.\run.ps1 -DataDirectory ./artifacts/manual-experiment
```

1. Open the **Position probes** tab, open disposable documents/windows, and choose **Refresh**.
2. With one active display, select eligible windows (Ctrl+click for several) and choose **Capture selected**. No windows move during capture.
3. Select exactly one captured window, choose a width and probe position, and choose **Move selected**. Requested and observed physical frames are shown separately.
4. Use **Reveal and focus** to bring that window back to the center. Windows may decline a foreground focus request; the status reports this.
5. Choose **Restore all**, use Ctrl+Alt+Shift+Escape, or close the workbench to restore original placements.

Only explicitly captured windows can be moved by the probe workbench. Multiple displays disable capture/movement. During a captured session, a display-count change triggers restoration on the next two-second check. The manager never minimizes, hides, or reparents apps as part of a probe. The provisional strip uses physical off-screen placement under the restrictions documented in the [preview guide](strip-preview.md); broader compatibility remains open.

If restoration fails, the journal retains that entry for retry and normal close is cancelled. **Exit keeping recovery** deliberately exits without further restoration; the next launch offers **Restore all** before a new capture can proceed. A malformed/unsupported journal is preserved and reported instead of replaced. Keep the recovery directory when investigating such a failure.

Default storage is `%LOCALAPPDATA%/ScrollWM/recovery.json`. The journal contains process/window identity, placements, frames, DPI, and recovery markers; it contains no window titles or document content. Do not commit local journals or diagnostic reports.

## Disposable fixture windows

This starts three simple resizable windows in a separate process. Each has a button that moves only itself, allowing a recovery test without managing an existing user app:

```powershell
.\run.ps1 -FixtureCount 3
```

The fixture mode does not acquire the manager lock. Close these windows before rebuilding. The native verification tool identifies them by an explicit window property, so a wrong PID cannot accidentally select an ordinary application.

## Native verification tool

Use the built apphost executable below, which carries the per-monitor-DPI manifest. Close the workbench first because the tool also acquires the manager lock. Replace `12345` with the PID of the fixture process.

```powershell
# Inspect candidates (metadata only; no capture or movement).
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe list

# One active display required. Tests all five positions, app minimum width,
# reveal/focus, and restoration for all three marked fixtures.
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-fixtures 12345 ./artifacts/native-probe ./artifacts/native-probe/report.json

# Capture without moving and wait. This can run with multiple displays because
# it only records placements. Move a fixture using its own button, terminate
# this worker, then restore from a fresh process with the command below.
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe capture-fixtures-and-wait 12345 ./artifacts/recovery-check

./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe recover ./artifacts/recovery-check
```

`park-fixtures <pid> <data-dir>` additionally captures and moves the fixtures off the right edge before waiting for termination. It requires one active display and is intended only for the M0 forced-termination experiment. Run `recover` with the same directory afterward.

The verification command's exit status reports execution/restoration errors; a successful exit does not mean every app accepted every target rectangle. Review the requested/observed frames and `Exact` values in its JSON report. Minimum-size constraints are deliberately measured, not suppressed.

## Selecting existing application windows

`inspect` reports visible top-level windows with process name, identity, native placement, and an exclusion reason. It does not read window titles or document contents. Reasons distinguish minimized/maximized windows, cloaked/off-desktop windows, non-resizable/shell surfaces, and elevated/inaccessible processes.

Save a JSON array containing the complete `Identity` objects for only the windows being tested. The probe revalidates handle, PID, process start time, thread, and class against a fresh inventory before capture; stale or ineligible selections are rejected before movement.

```powershell
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe inspect

./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-selected ./artifacts/selected-identities.json ./artifacts/selected-probe ./artifacts/selected-probe/report.json
```

The selected-window probe measures all five positions, all four width presets, minimum width, actual foreground focus, and original-frame restoration. It restores in a `finally` block and retains unsuccessful recovery entries.

To explicitly test a maximized window, append `--include-maximized`. This diagnostic option journals its original maximized placement before temporarily restoring it to normal geometry, then restores its maximized show state and original normal-placement coordinates afterward. It does not change the manager's default maximized-window exclusion. Minimized and cloaked windows remain excluded.

For an interrupted-worker experiment, `park-selected <identities.json> <data-dir>` captures normal eligible windows and parks them off-screen before waiting. Terminate only that probe worker, then run `recover <data-dir>` from a new process. The user application must remain running. This command intentionally leaves the journal pending until recovery.

## Verification record — 2026-11-07

Environment: Windows 11 x64; two active displays; observed fixture DPI 96 (100%). Project-local SDK 10.0.403. Tests ran through VSTest outside the restricted execution sandbox after its test-host connection was blocked inside the sandbox.

| Check | Result |
| --- | --- |
| Release solution build | Passed; all six projects compile |
| Core tests | 21 passed: widths, probe geometry, journaling failures, identity mismatch, partial capture/restore, and checkpoint retry |
| Windows persistence tests | 6 passed: fresh-store round-trip, atomic replacement path, malformed/null/unsupported journals preserved |
| Workbench UI | Inspected through computer use; controls visible and capture disabled with two displays |
| Single-display guard | Fixture edge verification refused before capture/movement because two displays were active |
| Cross-process recovery | Captured three marked fixture windows; moved the first using its own UI button; force-terminated the capture worker; a fresh process restored 3 entries, with 0 pending and no errors |
| Measured restoration | The changed fixture returned from visible origin (227, 160) to (67, 80); all three observed frames equaled their captured frames |
| Emergency shortcut dispatch | Ctrl+Alt+Shift+Escape reached the restore handler in an empty session and reported 0 pending |
| Single-display partial/full edge positions | Not run: requires one active display |
| 125% and 150% scaling | Not run |
| Browser/editor/terminal/Explorer compatibility | Not run; existing user windows were not repositioned during verification |
| Emergency shortcut while off-screen, focus restrictions, topology-change restore | Implemented paths; manual behavior checks still outstanding |

## M0 exit gate

M0 is **not complete**. The single-display fixture and Notepad/Chrome checks below pass. Complete editor, terminal, Explorer, and scaling coverage in [the manual test plan](manual-test-plan.md), record clamping/minimum-width/focus results, and finalize the strip's off-viewport policy before default automatic management/release. Experimental lifecycle development proceeds under [decision 0003](decisions/0003-opt-in-lifecycle-preview.md).

## Single-display follow-up — 2026-11-07

After the second monitor was disconnected, the native adapter reported one display with a 1920 × 1168 pixel work area. All three disposable fixtures reported 96 DPI (100%).

| Check | Observed result |
| --- | --- |
| Center and partial/full positions beyond both edges | All 15 normal targets matched exactly: 5 positions × 3 windows, each 960 × 1168 pixels |
| Left/right parking | Fully off-screen frames at x = -992 and x = 1952 were accepted without shrinking their 960 pixel width |
| Minimum width | A 40 pixel request was constrained to visible widths of 226, 266, and 306 pixels by the three fixtures; app constraints must feed back into layout |
| Reveal/focus | All three reveal focus requests were accepted by Windows; broader app focus behavior remains to be checked |
| Restore after edge probes | All 3 captured windows restored, with 0 pending entries and no errors |
| First forced-termination off-screen recovery | A journal replacement failure left 1 entry pending after 2 completed checkpoints; a fresh retry restored the retained entry |
| Journal-lock fix | Replacement now makes bounded retries for sharing/lock errors and Windows error 1175 while both files still exist; persistent failures still surface and retain recovery data |
| Regression tests | Real-file tests cover a brief lock released during retry and a persistent lock that preserves the original journal; total suite now 29 passing tests |
| Forced-termination retest after fix | Parked all 3 fixtures off-screen, terminated the worker, and restored through a fresh process: 3 restored, 0 pending, no errors; all measured frames matched their originals |
| Workbench emergency restore | Captured one fixture, moved it to observed frame (1952, 0, 960, 1168), then pressed Ctrl+Alt+Shift+Escape; 1 restored and 0 pending; original visible frame (227, 200, 506, 293) confirmed through computer use |

Raw probe/recovery reports remain under the ignored `artifacts/` directory. No existing user application windows were repositioned in these checks.

The provisional single-screen strategy is direct physical placement beyond the viewport while retaining normal window/show state. Keep observed minimum widths in the eventual layout state. The manager must reveal externally focused windows without interpreting off-screen parking as user minimization. Real-app compatibility and higher DPI are still required before adopting this strategy for automatic management.

The same technique does not establish safe clipping on adjacent displays. Two-screen support remains a required future milestone described in [the multi-monitor plan](multi-monitor-plan.md).

## Open application windows — 2026-11-07

These checks used the user's existing application windows on Windows build 26300, with one display, a 1920 × 1168 pixel work area, and 96 DPI. Only native window geometry/show-state/focus operations were issued; document and page contents were not read or edited.

| Application | Starting state | Position probes | Width presets | Observed minimum visible width | Focus | Restoration |
| --- | --- | --- | --- | --- | --- | --- |
| Notepad 11.2607.14.0 | Normal | 5/5 exact, including fully beyond both edges | 4/4 exact | 322 px for a 40 px request | Request accepted; actual foreground handle matched | Exact original visible frame and complete saved placement restored |
| Chrome 156.0.8078.80 | Maximized | 5/5 exact after explicit diagnostic normalization | 4/4 exact | 502 px for a 40 px request | Request accepted; actual foreground handle matched | Original maximized frame/show state, flags, min/max positions, and normal bounds restored |

The default selected-window command rejected the maximized Chrome selection before capture. Only the explicit diagnostic option allowed that test. Final inspection confirmed both applications' saved placements and visible frames matched the baseline exactly.

For the interruption check, the selected Notepad window was parked fully beyond the right edge, preserving its original width. The probe worker was terminated while Notepad stayed running. A new recovery process restored one entry, with zero pending entries and no errors, and measured the exact original frame afterward.

The inventory also reported the open editor and other browser windows as minimized, an Explorer application window as cloaked/on another desktop, and other surfaces as non-resizable or shell/tool windows. Those exclusions were inspected rather than treated as successful geometry coverage. Assistant/control windows were not selected for repositioning.

No user application was closed for the tests. Raw selections, inventories, and reports are retained only under ignored `artifacts/`. Release compilation passed with zero warnings/errors and all 29 regression tests passed after the diagnostic changes.

Remaining coverage: a normal editor window, terminal, and Explorer; Alt+Tab/user minimization lifecycle reconciliation (not implemented yet); 125%/150% DPI; topology changes. The Notepad/Chrome results support the provisional direct-placement approach at 100% scale, but do not complete the broader gate.
