# Compact UI verification

## Restored-window automatic adding — 2026-11-17

Automatic adding previously put all known windows into its exclusion baseline, including maximized windows absent from the selection list. The baseline now includes only windows eligible at Start or Resume. Previously ineligible windows can join when restored, while eligible windows deliberately left unselected remain outside. Foreground, location, and completed-move events request an inventory for unmanaged or suspended windows; ordinary managed location changes retain their existing fast path. The two-second fallback remains active.

Seven new regression cases failed before the fix: restored enrollment through fallback and four event types, restored enrollment after Resume, and a suspended column returning on a location event. All now pass. Additional coverage checks that automatic adding stays off by default, existing widths and background focus survive, and repeated reconciliation does not duplicate capture. The complete suite passes: 113 core and 9 Windows tests. The final Release solution build has zero warnings/errors.

Live GUI check at 100% scale on one monitor: opened two marked disposable windows, maximized the second before opening the preview, selected only the first, and enabled **Add windows automatically**. Start showed one managed window. Restoring the second via window activation automatically increased the strip to two, each at 946 px Half. The other five existing eligible windows remained outside. The checked option, updated label, Running state, and both rows were visually verified. Closing the preview restored the test windows before cleanup. This check used disposable windows rather than a browser; mixed-DPI and two-monitor restored enrollment were not exercised.

## Chrome single-monitor clipping — 2026-11-17

One 1920 × 1200 monitor at 100% scale was connected. Chrome reproduced the user's failure with the 6 pixel margin: Center passed, then LeftPartial failed because the 7 pixel invisible resize border extended past the screen and Chrome removed the imposed region. The failed probe restored the original frame and region exactly, with no pending recovery entries.

Single-monitor strips now use the same 8 pixel margin as multi-monitor strips. The repeated Chrome probe passed Center, both partial edges, both off-screen parking positions, and FullWidth. Repeating each placement caused no position or region writes. Every outer frame stayed on its owning screen or outside all screens; original placement and region restored exactly with no pending entries. Reports remain ignored under `artifacts/chrome-clip-before/` and `artifacts/chrome-clip-after/`.

The corrected GUI was tested with the actual Chrome, Visual Studio Code, WhatsApp, and File Explorer windows. Start, changing Chrome from Half to Two thirds, Next, Pause, and Resume completed without the clipping error. The Running state remained visible after resume. The tested GUI came from this worktree, rather than the older build in the original checkout. The app was left paused with all four windows restored.

Release build: zero warnings/errors. All 105 core and 9 Windows tests pass, including resize-border geometry at 100%, 125%, 150%, and 200% scale. All 16 disposable-window report checks pass, covering unchanged-layout/focus writes, overlap visibility, minimize/restore, pause/resume, and fresh-process crash recovery. Checks relating to a second display were not exercised; `TwoMonitorChecksExecuted` is false in `artifacts/chrome-clip-fixtures/report.json`. Hardware validation above 100% and the visual Alt+Tab flicker check remain pending.

## Focus redraw correction — 2026-11-10

The user reported flicker when switching windows and using Alt+Tab. The placement adapter previously assigned an empty region before every layout, including focus changes with no physical movement. It now keeps windows drawn, skips unchanged `SetWindowPos` calls, and reports whether placement/presentation actually changed. The coordinator skips its settlement delay for unchanged layouts. A size change can still refresh an outdated clip without first blanking the window.

- All six projects build in Release with zero warnings/errors. All 101 core tests pass, including visible-focus and overlapping-edge focus without settlement when physical frames stay unchanged.
- Chrome and File Explorer passed all 24 native position checks on the two connected monitors. Every repeated placement reported no position/region writes and no need to settle. Both original frames and regions matched after restoration, with zero pending entries.
- The expanded fixture verifier records layout activity around unchanged layout, visible focus, and edge reveal. Its fixture host could not start because Windows Application Control blocked `ScrollWM.App.dll` with `0x800711C7`; no fixture check passed in that run. Cleanup left zero pending entries.
- A visual Alt+Tab/focus check of the rebuilt GUI remains pending. Security settings were not changed. Earlier visual records below describe the previous build.

Raw reports remain ignored in `artifacts/focus-real-apps/report.json` and `artifacts/focus-redraw-check/report.json`.

## Edge overlap check — 2026-11-09

Windows 11, two monitors at 100%. Only two marked disposable windows were managed in the controlled UI session.

- Start showed two 946 px half-width columns.
- Width changed the focused column to 1261 px, Two thirds. The other window remained visibly exposed at the monitor edge beneath it, retaining 946 px.
- A screenshot of the edge window showed the expected overlap. Clicking its exposed area revealed the whole window. The manager showed the new focused row and unchanged 946/1261 px widths.
- Pause restored both windows. Native fixture checks separately verified ordering, monitor bounds, transfer, pause/resume, and crash recovery; see the [monitor record](multi-monitor-plan.md#edge-visibility-correction--2026-11-09).

The visual check covered the default window size. Minimum-size geometry was not changed by this feature; the newly worded help was not rechecked at minimum size.

## Multi-monitor check — 2026-11-09

Windows 11, two active side-by-side displays at 100% scale. The rebuilt preview was checked with one marked disposable window; existing user applications were not enrolled.

| Check | Result |
| --- | --- |
| Ready state with two displays | Passed; monitor selector shows primary monitor and start is available |
| Start only the selected fixture | Passed; Monitor 1 shows its focused 954 px half-width column |
| Move to next monitor button | Passed; Monitor 2 becomes selected and shows 1274 px, Half |
| Select the empty Monitor 1 | Passed; empty message appears; transfer, width, and navigation controls are disabled |
| Ctrl+Alt+M from the fixture on Monitor 2 | Passed; fixture returns to Monitor 1 at 954 px, Half; selector follows |
| Default 950 × 780 and minimum 760 × 620 | Passed; selector, transfer control, lists, actions, and emergency shortcut fit |
| Pause & restore after both transfers | Passed; reports one restored window, saved widths/order, and disabled layout controls; journal has zero pending entries |

The current broader native results and limitations are in the [multi-monitor guide](multi-monitor-plan.md). Mixed-DPI hardware, physical monitor changes, and ordinary application compatibility remain open.

## Original compact UI check

Date: 2026-11-07. Windows 11, one active display, 100% scaling.

The window keeps its 950 × 780 default size, 760 × 620 minimum, two list panes, and Position probes tab. Existing WPF buttons, lists, borders, and paths provide the controls and vector icons. The [SVG preview](assets/scrollwm-ui.svg) is an illustration with example names, not a desktop screenshot.

## Changes

- Put Start strip, Resume, and Pause & restore beside the current state. Show only the applicable actions. Pending recovery uses Restore windows.
- Put Refresh and addition controls beside Available windows. Keep focus, reorder, and Width controls below Strip.
- Show app names, stable per-session window numbers, effective widths, and width presets. Keep process IDs and handles in tooltips and diagnostics. Do not read window titles.
- Show Off, Running, Paused, Working, or Blocked explicitly. Paused rows show saved widths and order without an active focus marker.
- Disable navigation at strip boundaries and disable layout controls while paused or processing.
- Keep operation outcomes and errors visible through background reconciliation. Hide the message area when there is no message.
- Keep emergency restore visible. Put the full shortcut and manual-window instructions in an expander.

## Observed checks

| Check | Result |
| --- | --- |
| Ready and empty strip at 950 × 780 | Passed; default selection and Start strip are clear; layout controls are disabled |
| Ready state at 760 × 620 | Passed; both lists, actions, and emergency shortcut fit |
| Expanded help at 760 × 620 | Passed; the lists scroll and the footer stays visible |
| Position probes at 760 × 620 | Passed; controls and status fit |
| Start with one marked disposable window | Passed; Running state, focused row, and effective 954 px half-width appear |
| Width button | Passed; the row changes to 1272 px, Two thirds |
| Refresh and Add selected for a second disposable window | Passed; the second row is inserted at 954 px and the first retains 1272 px |
| Ctrl+Alt+Left | Passed; the focus marker and boundary controls update |
| Move right button | Passed; row order changes and widths are retained |
| Pause & restore | Passed; Paused state, Resume action, disabled layout controls, and restoration outcome appear |
| Native restoration comparison | Both fixtures' visible frames and saved placements match their recorded originals exactly |
| Resume | Passed; the two saved widths and reordered columns return |
| Ctrl+Alt+Shift+Escape | Passed; the strip pauses and reports restoration with strip shortcuts disabled |
| Normal preview close | Passed during the checks |
| Build and automated suite | Release build has no warnings/errors; 62 tests pass |

Only marked disposable windows were enrolled in the controlled test session. Raw restoration measurements are in ignored `artifacts/ui-check/`. Existing user app windows were not enrolled by those checks.

The UI Automation text sometimes lagged the rendered frame immediately after an asynchronous action. Checks used a fresh observation after the command settled. An occluded capture sometimes showed the foreground app; the target was reactivated before relying on the image.

The final paused-row label refinement was built after the workflow check. A repeat visual check was deferred when an active user strip was detected; that session was left running, and the additional disposable window was closed. The final running/empty-available state was observed in that live session.

## Still open

The blocked display/scaling states and pending-recovery/error appearance were not triggered in this visual check. Their guards remain in place. This pass did not complete native manual drag/resize, automatic enrollment, sleep/lock, high-DPI, or two-screen compatibility. Add open windows was checked for placement and enabled state; bulk enrollment was not invoked against the user's unrelated open apps.
