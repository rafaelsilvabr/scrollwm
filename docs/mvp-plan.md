# Functional MVP plan

Status: M1/M2 and an experimental multi-monitor implementation are available, 2026-11-09. Native gestures, session transitions, and the full compatibility matrix remain open. Working name: ScrollWM. Target release: `v0.1.0-alpha.1`. See [decision 0004](decisions/0004-multi-monitor-clipping.md), [the monitor verification record](multi-monitor-plan.md), and [the M0 record](m0-experiment.md).

## Outcome

A Windows user can open a browser, editor, terminal, and File Explorer, keep them at chosen widths, and move between them with shortcuts. Adding a fifth window preserves existing widths. Pausing or quitting restores the desktop's original window placements without closing apps.

This first milestone is about a usable native window manager. A simulated desktop or settings screen alone does not meet the MVP definition.

## Scope and defaults

| Area | MVP decision |
| --- | --- |
| Platform | Windows 11 x64, one or more monitors, normal user session |
| Multiple displays | Independent strips and clips. Pause/restore after a topology change; use visible fallback recovery for absent/reconfigured monitors. |
| Windows | Visible, normal top-level resizable application windows belonging to the current desktop; exclude owned dialogs, tool windows, shell surfaces, cloaked windows, and the manager's UI |
| Layout | One horizontal strip per monitor, one window per column, taskbar work area respected |
| Widths | One third, one half (default for newly managed windows), two thirds, or full usable width; preserve each existing window's choice |
| Navigation | Focus previous/next column and reveal it; stop at each end, without wrapping |
| Reordering | Swap focused column with its previous/next neighbor |
| Lifecycle | Insert a new eligible window immediately after the focused one; remove closed windows and close their gaps |
| Focus | Follow focus changes from Alt+Tab and taskbar clicks; reveal managed windows when focused |
| Manual changes | Respect app minimum sizes; resizing updates that window's stored width; dragging detaches it until explicitly re-added |
| User minimization | Remove the window from the active strip while retaining its record; restore/reinsert it when the user restores it |
| Maximization/full screen | Detach the window while maximized/full screen; do not force games or full-screen apps into the strip |
| Configuration | Local JSON in `%LOCALAPPDATA%/ScrollWM/`; width, gaps, exclusions, shortcut bindings |
| Controls | Tray menu: enable, pause/restore, resume, settings, quit; an emergency restore shortcut |
| Recovery | Capture original placements before any move; restore on pause/exit and offer recovery on the next launch after a crash |
| Distribution | Manually downloaded, self-contained Windows x64 ZIP, license, changelog, and SHA-256 checksum |

Proposed shortcut defaults (validate conflicts before finalizing):

| Action | Shortcut |
| --- | --- |
| Focus previous / next | Ctrl+Alt+Left / Right |
| Move column previous / next | Ctrl+Alt+Shift+Left / Right |
| Cycle width preset | Ctrl+Alt+W |
| Pause and restore / resume | Ctrl+Alt+P |
| Emergency restore and disable | Ctrl+Alt+Shift+Escape |

Failed shortcut registration must be visible in the UI. Never silently steal or assume a shortcut is available. Startup registration is opt-in and comes after manual enable works reliably.

## Explicitly deferred

- Full mixed-DPI, hotplug, and application-region compatibility remains a release gate for the implemented multi-monitor preview.
- Trackpad gestures, touch input, smooth/inertial scrolling, and animated transitions.
- Vertical stacks, tabs, grids, workspaces, and virtual-desktop management.
- Elevated app control, secure desktops, games, and exclusive full-screen windows.
- Installer, Microsoft Store publishing, auto-update service, cloud sync, and accounts.
- A public website or GitHub publication as part of the scaffold task.

## Milestones and exit gates

### M0 — Prove real-window positioning and recovery

Build a small opt-in native experiment before implementing the full management loop.

- Enumerate candidate windows and record process identity, handle, original placement, and DPI context.
- Test positioning with Win32 against Notepad, File Explorer, a browser, Windows Terminal, and an Electron editor.
- Test columns fully and partly beyond both screen edges. Document Windows clamping, invisible resize borders, minimum sizes, and focus behavior.
- Choose and document how off-viewport windows remain recoverable without being confused with windows minimized by the user.
- Prove the selected strategy on a single active display, including at 100%, 125%, and 150% scale.
- Restore placements from a journal after forced termination. Check identity before touching a handle because Windows can reuse handles.

**Exit:** a repeatable demo and written results show three or more real windows can retain their widths, reveal the focused window, and recover. If Windows constrains the intended model, revise the design and scope before production integration. Experimental preview development can proceed under [decision 0003](decisions/0003-opt-in-lifecycle-preview.md); the unverified application/DPI cases remain gates for default automatic management and release. Do not claim arbitrary off-screen placement or clipping is solved by merely calling `SetWindowPos`.

### M1 — Layout state and rules

- Model stable window identity, ordered columns, viewport offset, work area, and chosen widths.
- Implement insert-after-focus, remove, reorder, focus, width cycling, and minimal scroll-to-reveal.
- Define a deterministic startup order: focused eligible window first, remaining windows in enumeration order.
- Add tests for opening a fifth window without shrinking existing widths, edge navigation, closing the focused window, minimum widths, and an empty strip.
- Keep all calculations independent of native APIs.

**Exit:** deterministic tests cover the user-visible rules; viewport behavior is consistent with the M0 findings.

### M2 — Native window lifecycle

- Add candidate filtering and native event subscriptions for create/show, close/hide, focus, movement, resize, and minimize/restore.
- Reconcile an occasional fresh snapshot to recover from missed events; do not scan continuously at high frequency.
- Apply placement batches and ignore manager-generated events to prevent feedback loops.
- Handle windows closing mid-operation, app refusals to resize, taskbar changes, screen locking, and sleep/resume.
- Detect a display topology change, restore managed windows, and pause.
- Preserve original placements for every window first captured during the session, including windows created later.

**Exit:** the demo applications can be opened, focused with Alt+Tab, resized, and closed repeatedly without gaps, stale handles, or focus loops.

### M3 — Controls and everyday use

- Connect the WPF shell to a tray icon and explicit enable/pause/resume lifecycle. Never enable management just because the app launched.
- Register and validate configurable global shortcuts.
- Add local settings, app exclusions, and visible status/errors.
- Handle config errors by retaining defaults and surfacing an actionable message.
- Enforce a single manager instance per interactive session.

**Exit:** a user can run a complete session without a console and can always reach pause/restore.

### M4 — Recovery and compatibility

- Write the recovery journal atomically before the first placement change; keep it until restoration succeeds or the user explicitly dismisses an unrecoverable entry.
- Restore surviving windows on pause, normal exit, and detected unsupported display changes. Do not attempt to reopen apps that have closed.
- On restart after a crash, offer restore before allowing a new management session; explain that a terminated process cannot execute cleanup itself.
- Avoid logging window titles, document names, or content by default. Keep diagnostic logging local and opt-in.
- Complete the manual test matrix and fix reproducible compatibility failures.

**Exit:** emergency restore, clean shutdown, and next-launch crash recovery pass the manual checklist.

### M5 — First public alpha

- Run build, unit tests, and the real-desktop checklist on a clean Windows 11 x64 machine.
- Publish a self-contained ZIP locally and verify that it starts on a machine without a separately installed runtime.
- Inspect the package for private files; include the MIT license, changelog, known limitations, and usage/recovery instructions.
- Confirm name, copyright attribution, license, GitHub owner, repository visibility, and support/reporting routes before public publication.
- Create a GitHub release only when publication is requested; do not claim code signing if the build is unsigned.

**Exit:** `v0.1.0-alpha.1` is installable from the ZIP, documented, and recoverable. A functioning CI build alone is insufficient.

### M6 — Two-screen support: experimental implementation

Two-screen operation is a product requirement, confirmed on 2026-11-07 and requested for implementation on 2026-11-09. The preview now supports independent strips, region clipping, and transfers under [decision 0004](decisions/0004-multi-monitor-clipping.md). The full real-desktop exit matrix below remains open. Prioritize it before optional gestures, animation, and alternative layouts.

- Keep an independent strip, viewport, focus, and chosen window widths on each display.
- Route new windows to their owning display; moving through one strip must not reshuffle the other.
- Support an explicit command to transfer a focused window between displays.
- Respect each display's work area and DPI, including mixed scaling and monitors left of or above the primary display.
- Prevent partially or fully off-viewport columns from appearing on a neighboring screen. Removing the current display-count guard alone is insufficient.
- Handle monitor connection, disconnection, and primary-display changes without leaving windows inaccessible.
- Preserve recovery across topology changes, including a documented fallback when the original display is absent.

**Exit:** the two-display acceptance matrix in [the multi-monitor plan](multi-monitor-plan.md) passes. The basic native two-monitor clipping/transfer checks pass; mixed-DPI hardware, real topology transitions, and broader app/input compatibility still need verification. Position probes retains its separate one-monitor guard.

## Definition of a functional MVP

- [ ] With four real test apps open, enabling ScrollWM arranges eligible windows into a horizontal strip.
- [ ] Opening a fifth app inserts it after the focused column without altering previous chosen widths.
- [ ] Keyboard focus and reorder work at both strip boundaries; focused windows become reachable and visible.
- [ ] Width presets work subject to each app's minimum size; unsupported windows are skipped with a useful reason.
- [ ] Alt+Tab, close, minimize/restore, manual resize, and detach do not corrupt the strip.
- [ ] Taskbar boundaries and 100%, 125%, and 150% scale pass the real-display compatibility matrix.
- [ ] Multi-monitor clipping, transfers, and topology-change restoration pass the full native matrix.
- [ ] Pause, quit, emergency restore, and recovery after forced termination pass.
- [ ] Settings persist; bad settings and unavailable shortcuts do not leave windows inaccessible.
- [ ] No sign-in, server, or telemetry is required.
- [ ] CI passes, the release ZIP is tested, and limitations are documented.

## Performance targets to measure

These are acceptance targets, not measured claims: with ten ordinary windows on a documented reference machine, shortcut-to-placement latency should be under 100 ms at the 95th percentile over 100 operations; idle CPU should average below 1% over 60 seconds. Measure with animations disabled. Record hardware, Windows build, app versions, scale, and measurement method with results.

## Initial backlog

| Order | Work item | Depends on |
| --- | --- | --- |
| 1 | Run the M0 positioning/recovery spike and record the off-viewport strategy | Scaffold |
| 2 | Implement ordered strip state and viewport rules | M0 |
| 3 | Implement native discovery, filtering, and placement adapters | M0 |
| 4 | Connect event reconciliation to core state | 2, 3 |
| 5 | Add shortcuts, tray controls, settings, and exclusions | 4 |
| 6 | Finish restoration, recovery, and unsupported-display handling | 4, 5 |
| 7 | Run compatibility checks and prepare alpha package | 6 |

## Implementation progress

- [x] Draft scope, architecture, milestones, and release gates.
- [x] Create solution and separate app/core/native projects.
- [x] Add contribution guidance, MIT license, issue forms, and CI definition.
- [x] Add a welcome shell and initial width rule with unit test source.
- [x] Install a project-local SDK; build the solution and run the automated test suite.
- [x] Implement the opt-in M0 workbench, native window discovery, position probes, and recovery journal.
- [x] Verify the multiple-display refusal and capture/restore across forced worker termination using disposable fixtures.
- [x] Verify all 15 normal single-display fixture targets at 100% scale; widths remain unchanged at both off-screen edges.
- [x] Verify off-screen recovery after forced termination and emergency restore through the workbench.
- [x] Test the open Notepad and Chrome windows: all 18 normal position/width targets match, actual focus succeeds, original show states and placements restore exactly; interrupted-worker Notepad recovery also passes.
- [x] Implement ordered strip state, minimal viewport scrolling, insertion, removal, reordering, width cycling, and minimum-width feedback.
- [x] Add explicit selected-window strip enablement, manual enrollment, pause/resume, serialized recovery-aware commands, and keyboard bindings.
- [x] Verify a clean Release build and 62 automated tests; previously verified all nine native five-window strip scenarios with complete survivor restoration.
- [x] Verify selected-window start/add, width, Ctrl+Alt+Left, button reorder, pause/resume, emergency restore, and normal close through the compact UI; see [the UI record](ui-verification.md).
- [ ] Complete the remaining shortcut, native gesture, and compatibility matrix.
- [x] Add native lifecycle subscriptions, coalesced reconciliation, opt-in new-window enrollment, minimize/restore reinsertion, manual resize adoption, and drag detachment.
- [x] Verify native creation, minimize/restore, destruction, callback health, and listener shutdown with a marked disposable window.
- [ ] Verify native event-to-layout behavior, manual drag/resize, lock/unlock, and sleep/resume on one display.
- [ ] Complete the real-application/DPI matrix and finalize the off-viewport strategy before default automatic management/release.
- [ ] Implement settings, exclusions, and tray controls.
- [x] Implement independent monitor strips, scaled physical geometry, recoverable clipping, transfers, and version 2 monitor/region recovery metadata.
- [x] Verify native placement, clipping, independent updates, transfer, and exact restoration with five disposable windows on two connected 100% monitors.
- [ ] Finish the mixed-DPI, native input, real hotplug, and application compatibility matrix for M6.
