# Multiple monitors

Status: experimental two-monitor implementation, 2026-11-09. The full M6 compatibility matrix remains open. See [decision 0004](decisions/0004-multi-monitor-clipping.md).

## Use

1. Start ScrollWM with both monitors connected. Eligible windows are selected by default.
2. Select **Start strip**. Windows join the strip on the monitor where they were captured.
3. Choose a monitor above the strip list to view and control its columns. Shortcuts follow newly focused managed windows, including focus changes from Alt+Tab.
4. Use **Move to next monitor** beside the selector, or **Ctrl+Alt+M**, to transfer the focused column. Existing destination widths remain unchanged. With more than two monitors, transfer cycles through the selector's order.
5. Use **Pause & restore**, emergency restore, or normal close to restore all monitors' captured windows.

**Add open windows** routes each new candidate to its own monitor. Automatic enrollment uses the same ownership rule. Resizing changes that column's width. Dragging to another monitor transfers ownership; dragging within the same monitor detaches the column until explicitly re-added. Native drag compatibility remains an open verification item.

If a monitor is connected, disconnected, reconfigured, or changes scale, management pauses and restores. Select **Start strip** to create strips for the new configuration; resuming stale monitor geometry is disabled.

## How windows stay on their monitor

Each strip has an independent viewport, order, focus, and widths. Edge columns keep their sizes but shift inside their owning viewport and sit behind fully visible columns. Their exposed portions remain visible and clickable. Clicking an edge window, Alt+Tab, or keyboard navigation reveals it fully. Only entirely off-viewport columns park beyond every connected screen. Logical strip positions stay separate from physical positions; compositor clipping is not required to keep a window out of a neighboring monitor.

When widths do not fit side by side, overlap can cover the normal inter-column gap and shows the exposed part of the shifted window rather than a cropped off-screen surface. The user selected this behavior to preserve window widths. ScrollWM lowers only edge windows beneath managed full windows on the same monitor, without activating windows or raising them above unrelated apps. Incompatible always-on-top groups stop management and restore rather than change an uncaptured topmost setting.

Parking prefers an exterior edge nearest the owning monitor to avoid unnecessary DPI changes. Windows must accept the physical placement; clamping or a window returning onto a connected screen stops management and restores originals. The native adapter exposes logical positions separately from physical measurements, while retaining actual size feedback for minimum widths. Neither the original recovery capture nor window show state changes because of parking.

Clipping verification accepts app-owned shapes contained in the permitted rectangle. Rounded or smaller regions do not need to equal the manager's rectangle. A missing explicit region is accepted only when the whole outer window fits; an empty region is accepted only for a fully hidden column. Unsafe clipping still pauses and restores, naming the application to exclude before starting again.

Isolation is verified from physical outer frames: parked columns must be outside all monitors, and visible windows must fit inside their owner. Neither case depends on a partially clipped compositor surface. A window too large to fit causes pause/restoration instead of leaving the focused window hidden.

The recovery journal saves original placements, window regions, outer bounds, and monitor context before movement. Restoring returns the original shape as well as the placement. Existing version 1 journals remain readable. New captures write version 2, which older builds cannot read; recover with the current build.

Exact restoration is used when the original monitor context is unchanged. If that monitor is absent or reconfigured, recovery centers the original logical size in the available work area, using the matching monitor or primary monitor. An unverified or rejected restore stays pending. Applications whose minimum size or custom region behavior prevents safe placement may require pause/recovery instead of continued management.

The 6 pixel single-monitor margin, 8 pixel multi-monitor margin, and 12 pixel gap are logical defaults at 96 DPI and scale independently. The extra multi-monitor margin leaves room for invisible resize borders. Width presets include gaps: two halves and three thirds fit as whole windows. Custom widths and learned minimums scale by the source/destination DPI ratio when transferred; adding windows preserves existing widths.

## Verification

Run the fixture tool with the preview closed:

```powershell
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-monitors ./artifacts/monitor-check ./artifacts/monitor-check/report.json
```

The tool opens only its own marked disposable windows, distributes them between connected monitors, and checks actual native frame and window-region bounds. It exercises independent layout, transfers, minimize/restore, original custom shape restoration, and recovery through a terminated worker and fresh recovery process. It closes its fixtures afterward. Internal worker modes operate only on marked fixtures under the parent verification process's manager lock.

Native verification on 2026-11-09 passed all 13 checks on two side-by-side monitors at 100% scale: 1920 × 1200 and 2560 × 1080, with their taskbar work areas respected. Checks include independent navigation/width changes, no region bounds outside the owning monitor, minimize/restore, transfers preserving originals, custom shape restoration, pause, resume, and fresh-process crash recovery. Pause and crash recovery each restored all five fixtures with zero entries pending and no errors. Raw reports stay in ignored `artifacts/`.

The initial Release build passed with zero warnings/errors and 70 core tests. The latest parking correction passes 92 core tests, the 13 native fixture checks, and ten Chrome position checks, as recorded below. The nine Windows persistence tests passed in the earlier staging run; Windows Application Control blocked that assembly on the default-output rerun. Security settings were not changed. The latest full-suite run therefore remains incomplete.

Deterministic tests cover independent updates, background enrollment, negative origins, 100%/150% geometry, preset/custom transfer sizing, foreground routing, suspension, monitor removal, and visible recovery when an original monitor is absent.

## Acceptance matrix

| Scenario | Status |
| --- | --- |
| Two monitors side by side at 100% | All 13 native fixture checks passed |
| Independent navigation, resize presets, and additions | Core tests; native independent navigation/width checks passed |
| Transfer focused window and preserve original captures | Core and native checks passed |
| Secondary monitor left of or above primary | Negative-coordinate core tests; physical arrangement not changed |
| Mixed 100%/125%/150% DPI | Core scaling tests; physical mixed-DPI run pending |
| Manual cross-monitor dragging | Core transfer policy tested; native input verification pending |
| Monitor disconnect/reconnect or primary change | Pause/recovery logic tested; real hardware transition pending |
| Missing-monitor recovery | Pure placement policy tested; native hardware case pending |
| Minimize/restore without drawing on the other monitor | Native fixture check passed |
| Force-terminate manager and recover original shapes | Fresh-process native recovery restored all five fixtures; no entries pending |
| Selector, transfer button, Ctrl+Alt+M, compact layout | [UI check passed](ui-verification.md#multi-monitor-check--2026-11-09) at default/minimum size |
| Pause/quit/emergency restore | Per-monitor core recovery and native pause/resume checks; full UI matrix pending |
| Browser/editor/terminal/Explorer region compatibility | Pending |

M6 remains open until its real-desktop matrix passes. The preview supports multiple monitors; the Position probes diagnostic workbench retains its separate single-monitor restriction.

## Clipping failure follow-up — 2026-11-09

A reported start/resume failure restored five windows with zero pending entries. The exact-region-equality check was replaced with rectangular containment, with regressions for app-shaped bounds, adjacent-screen overflow, unexpectedly empty regions, and windows without an explicit region. The corrected implementation builds with zero warnings/errors and all 80 core tests pass. This addresses an overly strict check; it is not yet confirmation that the reported app is compatible.

The ordinary-app native diagnostic was initially blocked before movement by Windows Application Control (`0x800711C7`). Security settings were left unchanged. After the subsequent parking implementation, the rebuilt verifier ran through its normal path and passed the native checks recorded below.

For an explicit selection of ordinary applications, close the preview and save complete window identities from `inspect` as described in [the diagnostic guide](m0-experiment.md#selecting-existing-application-windows). Then run:

```powershell
./tools/ScrollWM.Spike/bin/Release/net10.0-windows/ScrollWM.Spike.exe verify-monitor-selected ./artifacts/selected-identities.json ./artifacts/selected-monitor-check ./artifacts/selected-monitor-check/report.json
```

This mutating diagnostic moves only the selected windows through centered, partial, and hidden positions on each connected monitor. It journals originals first and restores in a `finally` block. The report identifies the failing application and placement, records observed clip bounds, and compares restored frames and regions. It does not close ordinary applications.

## Persistent overflow follow-up — 2026-11-09

The user reported a long strip continuing to occupy the neighboring display while Running. Fully hidden columns now park outside every connected monitor instead of relying on an empty region at a logical position on another screen. Partly visible columns continue to use a verified monitor clip.

- All six projects build in Release with zero warnings/errors; all 92 core tests pass. Parking regressions cover long-strip isolation, independent monitor updates, focus reveal, width preservation, exterior-edge choice, negative origins, and vertical arrangements.
- The updated native fixture verifier passed all 13 checks on the two connected 96-DPI monitors, including actual physical parking positions, clipping, transfer, pause/resume, and fresh-process crash recovery. Pause and crash recovery each restored all five fixtures with zero pending entries and no errors.
- The open blank Chrome window passed all ten explicit native position checks: centered, left/right partial, and left/right fully hidden on both monitors. Hidden frames were physically outside both monitors; partial clips stayed inside the owner. Its original frame and region matched after restoration, with zero pending entries.
- These are native geometry/region measurements, not a visual inspection of every app's compositor output. Mixed DPI, physical topology transitions, ordinary-app long-running compatibility, and native gestures remain open.

Raw reports remain ignored in `artifacts/monitor-parking-check/report.json` and `artifacts/monitor-parking-browser/report.json`.

## Edge visibility correction — 2026-11-09

Hiding every partial column made neighboring windows disappear. The user selected width-preserving overlap beneath the focused window. Edge columns now stay inside their monitor at their existing size; full columns remain above them. Focusing an exposed edge returns it to the logical strip. Fully hidden columns remain parked. Interactive gesture origins use physical frames so a cancelled drag on a shifted edge does not detach it.

- All six projects build with zero warnings/errors, and all 100 core tests pass.
- All 15 native fixture checks pass on two 100% displays, including actual edge ordering, edge visibility inside the owning monitor, reveal without width changes, independent strips, transfers, pause/resume, and fresh-process recovery. Both recovery paths restore five fixtures with zero pending entries.
- All 36 native Chrome, VS Code, and Xournal++ placement checks pass. Original frames and regions restore exactly; zero entries remain pending.
- The controlled UI check enrolled only two disposable windows. Two halves appeared at 946 px each. Enlarging one to 1261 px left the 946 px neighbor visibly exposed beneath it. Clicking the exposed area revealed the neighbor, updated the focused row, and retained both widths. Pause restored both windows.

Current reports stay ignored in `artifacts/edge-overlap-check/`, `artifacts/edge-overlap-apps/`, and `artifacts/edge-overlap-ui/`. Earlier records below describe superseded policies. Mixed DPI, hardware transitions, broad application compatibility, and full native gesture coverage remain open.

## Partial-window overflow correction — 2026-11-09

The preceding parking correction reduced the overflow but did not resolve partly visible compositor surfaces. The current policy parks those partial columns too and verifies full outer bounds rather than treating a valid region as proof of visible isolation. Keyboard navigation or Alt+Tab reveals a parked window fully before activation. Presets now include gaps so two half-width columns can remain visible together.

An ordinary-app test also exposed VS Code reporting outer bounds through DWM while parked. Parked measurements now use the actual outer rectangle and known border offsets, preserving real size feedback and detecting position changes. The first failing diagnostic restored all three selected apps before this correction.

- Release build: all six projects, zero warnings/errors. All 99 core tests pass.
- All 14 updated native fixture checks pass on the connected 100% monitors. They include partial-column physical parking, outer-frame isolation, navigation/width changes, transfer, pause/resume, and fresh-process crash recovery. Both recovery paths restored all five fixtures with zero pending entries.
- All 36 native checks pass for the selected Chrome, VS Code, and Xournal++ windows: centered, left/right partial, left/right hidden, and full-width on both monitors. Every outer-frame isolation check passed. All three original frames and regions matched after restoration; zero entries remain pending.
- The running user session was paused and reported five restored windows before rebuilding. No documents or tabs were edited. Native measurements cover the tested apps/configuration; the mixed-DPI, topology, gesture, and long-running compatibility matrix remains open.

Current reports remain ignored in `artifacts/whole-window-final2/report.json` and `artifacts/whole-window-apps-final/report.json`. Earlier records above are historical and do not establish compositor isolation for partial windows.
