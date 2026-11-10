# Experimental scrolling strip

Status: experimental multi-monitor preview. Requires Windows 11 x64. Each monitor has an independent strip and clip; see [the monitor guide](multi-monitor-plan.md) and [decision 0004](decisions/0004-multi-monitor-clipping.md). Broader application, mixed-DPI, and topology compatibility remain open.

## Start and use

From PowerShell in the repository root, run the launcher. It uses the existing Release build or builds with the local/installed SDK when needed:

```powershell
.\run.ps1
```

1. On **Scrolling strip**, all eligible open windows are selected by default. Click **Start strip** to use them all, or Ctrl+click to adjust the selection first. **Refresh** reloads and selects all eligible candidates; while running, it lists only windows outside the active strip for **Add selected**.
2. Optionally check **Add new windows automatically**, then choose **Start strip**. The option starts unchecked. Originals are journaled first. Windows form a horizontal strip at half-width with 6 pixel outer margins and 12 pixel gaps between windows. A selected foreground window comes first; the other selected windows retain their order from the candidate enumeration.
3. Use the controls or shortcuts below. Focused columns are revealed by scrolling the strip; existing widths remain unchanged when another window is added.
4. Open another app and click **Add open windows** to discover and add all eligible windows outside the active strip in one step. New columns join after the focused column; existing widths stay unchanged. For individual additions, use **Refresh**, adjust the selection, then **Add selected**. With automatic enrollment enabled, newly opened eligible windows join without clicking either button. All additions are captured before they move.
5. **Pause & restore** returns captured windows to their original placements. **Resume** retains the strip's order and chosen widths and captures the windows' current paused placements as the new restoration baseline.
6. With multiple monitors, choose a monitor above the strip list. **Move to next monitor** or **Ctrl+Alt+M** transfers its focused column. Other monitors keep their widths and order.

The state badge shows Off, Running, Paused, Working, or Blocked. The strip list shows app names and effective widths. Hover a row for process details. Focus and reorder controls sit below the strip; they are disabled at its boundaries. Open **Shortcuts & window behavior** for the full help. See the [SVG preview](assets/scrollwm-ui.svg) and [visual check record](ui-verification.md).

### Nothing moves after launch

Opening the preview alone does not enable management. Eligible open windows are selected automatically; click **Start strip** to arrange them. The banner above the controls shows whether management is off, how to start, or the requirement blocking activation. **Start strip** stays disabled until at least one window is selected and the display, scaling, recovery, and shortcut requirements are satisfied.

If an older build reports that two displays are blocked, close it and run `./run.ps1 -Build`. The current strip preview supports multiple monitors. Normal resizable windows can be selected; minimized and maximized windows must first be restored. Position probes remains a single-monitor diagnostic tool.

`./run.ps1 -FixtureCount 5` only opens disposable test windows. Run `./run.ps1` separately to open the manager, then select those test windows and start the strip. If using older binaries, close ScrollWM and run `./run.ps1 -Build`.

| Action | Shortcut |
| --- | --- |
| Focus previous / next column | Ctrl+Alt+Left / Right |
| Reorder focused column | Ctrl+Alt+Shift+Left / Right |
| Cycle third / half / two-thirds / full width | Ctrl+Alt+W |
| Pause / resume | Ctrl+Alt+P |
| Emergency restore and disable strip shortcuts | Ctrl+Alt+Shift+Escape |
| Move focused window to next monitor | Ctrl+Alt+M |

Navigation stops at either end. The default width is half, so the first cycle goes to two thirds. App minimum widths override presets and are reflected in the next layout pass. A window wider than the viewport is aligned at its left edge when focused.

Minimizing a window temporarily removes its column. Restoring it reinserts the column with its chosen width and order. Maximized or otherwise ineligible managed windows are also suspended until eligible again; their originals remain recoverable.

Resize a managed window to choose a custom width. Dragging within its monitor detaches it, leaving your placement intact on pause/exit. Use **Add selected** to bring it back; that captures your placement as its new original. Dragging to another monitor transfers the column. While a native drag/resize is active, layout commands wait until input ends. Native gesture compatibility remains part of the manual matrix.

Automatic enrollment never takes over existing unselected windows. On resume, windows opened while paused become part of the unselected baseline; add them explicitly if wanted. You can change the enrollment option while paused. A new eligible background window does not request focus; newly foregrounded managed windows are revealed.

Shortcuts are registered as a group before enablement. If a binding is unavailable, the error identifies it and startup does not move windows. The emergency binding must also be available. Paused sessions retain the regular bindings for keyboard resume; emergency restore releases them. Closing the app restores windows before exit. If restoration remains pending, close is cancelled and the Position probes tab offers **Exit keeping recovery** for a deliberate later recovery.

## Preview limits

- Automatic enrollment is opt-in for each developer-preview session. It is not enabled at app launch or a release-readiness claim.
- Native events are coalesced for 80 ms, with a two-second fallback for missed notifications. Closed windows are retired from the recovery journal; suspended windows retain their records. A listener failure pauses and attempts restoration.
- The new lifecycle/gesture policies have coordinator coverage, but end-to-end event-to-layout and interactive gesture verification is still pending on one display. Lock/sleep transitions and full-screen compatibility are not yet verified.
- A monitor configuration, identity, clipping, or placement failure stops management and attempts restoration on all monitors. Failed entries remain pending. If the original monitor is absent or changed, recovery uses the documented visible-work-area fallback. Start new strips after a topology change.
- Width/order state is in memory. Preferences and strip persistence across app restarts are not implemented; the recovery journal persists independently.
- No tray UI, automatic startup, touch/trackpad gestures, animation, or full-screen management is included.
- Native frame settlement is awaited between layout attempts; the MVP latency/idle-CPU targets have not yet been measured for this preview.

The **Position probes** tab retains the [M0 diagnostic workbench](m0-experiment.md). Pause/restore before switching between strip management and captured diagnostic probes.

## Verification

Core tests cover insertion of a fifth window without shrinking existing widths, minimal scrolling, boundaries, reordering, removal/focus fallback, minimum widths, negative origins, and handle reuse. Coordinator tests cover capture-before-move, additions, failure restoration, pending recovery, pause/resume, display/DPI guards, opt-in enrollment and its baseline, disappearance during capture, out-of-order minimize/restore, resize adoption, detachment/re-addition, deferred commands, and missed gesture-end recovery. Journal tests verify durable removal before marker release.

To verify native event delivery, close the preview and run:

```powershell
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-window-events ./artifacts/window-events/report.json
```

This command creates one marked disposable window, minimizes/restores/closes it, and checks listener health and shutdown. It leaves existing application windows alone and can run with multiple displays because it does not arrange a strip.

For a native five-window exercise, start the disposable fixture mode:

```powershell
.\run.ps1 -FixtureCount 5
```

Close the preview window before running the mutating verification tool. Substitute the returned fixture PID:

```powershell
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-strip-fixtures 12345 ./artifacts/strip-check ./artifacts/strip-check/report.json
```

This verifies four-window layout, fifth-window capture/insertion, reveal/navigation, reorder, width cycling, pause/restore, resume, closure of one marked disposable window, and restoration of the four survivors. It never closes unmarked application windows. A background focus request may be declined by Windows; the check accepts a correctly revealed target and the visible focus-declined status, and interactive keyboard focus must be checked separately.

### Verification record — 2026-11-07

- Release build: all six projects compile with zero warnings/errors.
- Automated suite after lifecycle implementation: 62 passed (54 core/state/session/recovery tests and 8 Windows persistence tests).
- Native event verification: all 6 checks passed with two displays connected. Creation, minimize, restore, destruction, callback health, and listener shutdown were observed. No user application windows were moved. Event-to-layout integration and interactive manual input remain pending on one display; the multiple-display guard was retained.
- Native fixture check: all 9 scenarios passed on one display at 96 DPI. Initial four-window layout, fifth-window insertion, navigation/reveal, reorder, width cycling, pause of all five, resume, and closure removal were verified. Final restoration reported 4 restored, 1 closed/replaced, 0 pending, and no errors. Survivor frames matched their original measurements.
- A closure test exposed DPI zero after destruction; eligibility/identity is now checked before treating a DPI value as a scale change. The coordinator regression uses DPI zero for a closed window.
- The initial computer-use check failed to activate the window. A later [compact UI verification](ui-verification.md) passed start, selected addition, width, Ctrl+Alt+Left navigation, button reordering, pause/resume, emergency restore, and normal close with disposable windows. Both restored frames and saved placements matched their originals. Manual gestures and the remaining shortcut/compatibility matrix are still open.

Raw native reports remain in ignored `artifacts/strip-preview-verified/report.json` and `artifacts/window-events/report.json`. No existing user application was enrolled in the strip or event integration checks.

### Verification record — 2026-11-09

- Release build: all six projects compile with zero warnings/errors. All 70 core tests pass.
- The nine Windows persistence tests passed in an earlier staging run. Windows Application Control blocked their assembly on the final default-output rerun; the latest full-suite run is incomplete.
- All 13 native multi-monitor fixture checks passed on two monitors at 96 DPI. Independent placement, clipping, navigation/width isolation, transfer ownership, original captures, minimize/restore, custom shapes, pause, and resume were verified.
- Pause and fresh-process recovery after forced worker termination each restored all five disposable windows, with zero pending entries and no errors. Only the verifier's own marked fixtures were managed.
- Mixed-DPI hardware, physical topology changes, native cross-monitor dragging, and the ordinary application compatibility matrix remain open. See the [multi-monitor guide](multi-monitor-plan.md).

Raw multi-monitor measurements remain in ignored `artifacts/multi-monitor-final/report.json`.

### Persistent overflow correction — 2026-11-09

Fully hidden columns now park beyond all connected monitors and return to their logical strip before focus. The corrected Release build has zero warnings/errors and passes 92 core tests. All 13 updated native two-monitor fixture checks pass, including physical positions and fresh-process recovery. Ten native Chrome position checks also pass; the original frame/region restored exactly. Recovery left zero pending entries. See [the follow-up record](multi-monitor-plan.md#persistent-overflow-follow-up--2026-11-09) for the measured scope and remaining visual/application matrix.
