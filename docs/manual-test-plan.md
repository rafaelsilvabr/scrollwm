# Real-desktop verification

Run this after native integration exists. No scenario below has passed merely because the scaffold builds. Use disposable test documents and record the Windows build, hardware, app versions, display scale, commit, and outcomes.

## M0 experiment record

Record actual window rectangles before/after each move, focus results, Windows-imposed clamping, minimum widths, restore results, and the selected off-viewport strategy. Include partly visible and fully off-screen columns on both edges. Mark the architecture blocked for redesign if reachable/recoverable windows cannot be guaranteed under the chosen model.

## Matrix

| Area | Scenarios | Expected result |
| --- | --- | --- |
| Applications | Notepad, File Explorer, Edge or Chrome, Windows Terminal, VS Code | Eligible normal windows participate; dialogs and unsupported windows do not |
| Layout | Open 3, then 5, then 10 windows; close the focused/middle/last window | Existing widths stay unchanged; insertion/removal preserves correct order |
| Navigation | Repeat previous/next; navigate both boundaries; reorder both directions | Focus stays reachable, no wrapping, neighboring columns swap correctly |
| Width | Cycle all presets; use an app with a large minimum width | App constraints are respected without repeated resize loops |
| External focus | Alt+Tab, taskbar click, app-created window | Correct managed column is revealed without focus oscillation |
| User actions | Minimize/restore, resize, drag out, maximize, enter full screen | Behavior matches the scope table; no forced full-screen tiling |
| Shell filtering | Desktop, taskbar, tooltips, dialogs, manager settings | Never adopted into the strip |
| Display | One/two screens at 100%, 125%, and 150%; negative origins; change taskbar placement | Independent viewports, correct physical bounds, no cross-monitor drawing |
| Topology | Start with two displays; attach/remove/reconfigure while enabled | Two strips start; topology changes pause and restore, with visible missing-monitor fallback |
| Shortcuts | Existing registration conflict; change binding | Visible actionable failure, no silent partial enablement |
| Lifecycle | Enable, pause, resume, quit; launch a second instance | Original placements restored; a second manager cannot take over |
| Failure | Close app during batch; invalid config; unwritable journal | Clear error, no unsafe move without recoverable original state |
| Recovery | Force-terminate manager, reopen it, request restoration | Original live windows recover; reused handles/closed apps are skipped safely |
| Session | Lock/unlock, sleep/resume | Reconcile and preserve recoverability |
| Privilege | Open elevated and unsupported windows | Skip with a reason; do not repeatedly request elevation |
| Packaging | Clean Windows 11 x64 machine without the .NET runtime | Self-contained package launches and completes a normal management session |

## Lifecycle preview checklist

Use one display at 100% scale and disposable windows. The native event-delivery check and coordinator tests do not mark these end-to-end cases passed.

- [ ] Leave automatic enrollment unchecked, start two selected windows, then open another: it remains unselected until **Add selected**.
- [ ] Enable automatic enrollment, start two selected windows with another already open, then open a fourth: only the new eligible window joins; chosen widths remain unchanged.
- [ ] Create a background window: focus remains with the current app. Alt+Tab to a managed off-viewport window: its column becomes visible.
- [ ] Pause, open a window, resume: it remains outside the strip. A window opened after resume joins when enrollment is enabled.
- [ ] Minimize several columns and restore them in reverse order: widths and logical order survive. Maximize and restore a managed window without forcing it back while maximized.
- [ ] Resize a column, including against an application's minimum width: its custom width is retained and neighbors keep their widths.
- [ ] Drag a column without resizing: it detaches, stays where placed, and is not moved by pause/exit. Re-add it, then pause: it returns to the placement captured at re-addition.
- [ ] Cancel a drag after issuing a navigation command: no snap occurs during input; the deferred command completes after input ends.
- [ ] Repeatedly open/close eligible windows and close one during layout: recovery remains valid, gaps close or management safely pauses, and no reused handle is touched.
- [ ] Start with two monitors, then attach/remove/reconfigure one while active: independent strips start; configuration changes restore and pause; missing-monitor windows return to a visible fallback.

Current verification: native fixture event delivery passes creation, minimize/restore, destruction, callback health, and shutdown. The new full desktop lifecycle cases above remain pending; see [the preview record](strip-preview.md#verification-record--2026-11-07).

## Release checklist

- [ ] Build and unit tests pass on the release commit.
- [ ] All MVP matrix cases pass or have an explicit documented exclusion consistent with the MVP scope.
- [ ] Keyboard navigation with ten windows meets the measured targets in the MVP plan.
- [ ] Pause/exit restoration and next-launch recovery pass independently.
- [ ] Package contains license, changelog, and accurate limitations.
- [ ] No window titles, document names, secrets, private paths, or local recovery files are committed or packaged.
- [ ] Release checksum generated and independently checked.
- [ ] Working name, license, GitHub ownership, visibility, and reporting routes confirmed before publication.

## Required two-screen follow-up

The experimental implementation passes 13 native fixture checks with two monitors at 100% scale, including clipping, independent strips, transfers, and fresh-process recovery. Complete the separate [multi-monitor acceptance matrix](multi-monitor-plan.md) before release, including mixed DPI, native gestures, ordinary application compatibility, and physical disconnect/reconnect.
