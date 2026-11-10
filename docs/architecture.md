# Architecture

Status: draft. Read [decision 0001](decisions/0001-windows-native.md) for the stack choice.

## Project boundaries

| Project | Responsibility | References |
| --- | --- | --- |
| `ScrollWM.App` | WPF lifecycle, tray UI, settings, shortcut commands, orchestration | Core, Windows |
| `ScrollWM.Core` | Strip state, column widths, viewport calculation, commands, and platform-neutral contracts | .NET base library only |
| `ScrollWM.Windows` | Win32 discovery/filtering, event hooks, display/DPI information, placement, focus, and restoration | Core |
| `ScrollWM.Core.Tests` | Deterministic layout/state tests without moving desktop windows | Core |

The app is the composition root. The core never references WPF, HWND-specific APIs, the filesystem, or a running desktop. Native adapters translate handles and events into core identities and commands. Keep these as three small projects; no dependency-injection framework or additional service projects are needed for the MVP.

## Intended data flow

1. Native adapters capture eligible windows and display work area.
2. The coordinator records original placements before applying changes.
3. Shortcut commands and normalized native events enter one serialized state-update queue.
4. Core rules update ordered columns and compute target rectangles.
5. Native adapters apply a placement batch and report refusals or vanished windows.
6. The coordinator reconciles results and updates visible status.

Tag or suppress manager-initiated placement events so they do not trigger recursive layouts. Coalesce event bursts. Never block native callbacks with disk writes or long layout work.

## Planned state

- **Window identity:** handle plus process identity/start information, checked before restore to guard against handle reuse.
- **Column:** window identity, chosen width, effective minimum width, logical position.
- **Strip:** ordered columns, focused identity, viewport offset, usable work area.
- **Session snapshot:** original placement, show state, and capture timestamp for each window before its first managed move.
- **Lifecycle:** disabled, enabling, enabled, restoring, paused, recovery required. Enter enabled only after capture/journaling succeeds.

Persist restoration data separately from preferences. A successful restore can remove its entry; a failed entry must remain recoverable. During graceful shutdown, unregister shortcuts and hooks, restore live windows, then dispose the tray icon. Forced process termination requires recovery on the next launch.

## Coordinates and display policy

Use a per-monitor-DPI-aware process and physical screen pixels at the native boundary. Convert WPF device-independent units only for UI. Account for the taskbar work area and invisible window borders when comparing visible frames.

Width presets resolve against the usable strip width after outer margins. Interior gaps are additional spacing between columns; the total strip is allowed to exceed the viewport. Window minimum-size constraints override presets and feed their effective widths back into layout.

The preview keeps identified monitor configurations and independent strips. Physical window regions constrain managed drawing to the owning monitor, including fully off-viewport columns. Gap/margin values scale per monitor. A topology or scaling change pauses all strips and restores captured windows, with a visible fallback when an original monitor is absent or changed. See [decision 0004](decisions/0004-multi-monitor-clipping.md) and [the monitor verification record](multi-monitor-plan.md). Position probes retains its one-monitor guard.

Edge columns shift inside their owning viewport without resizing and overlap beneath fully visible columns. Only entirely off-viewport columns park beyond every connected screen. Logical order, positions, and widths remain separate from physical placement. The adapter lowers edge windows beneath the lowest full-window anchor without activating windows or changing topmost groups. Reconciliation maintains this ordering; focus reveals an edge fully. Presets account for gaps so two halves fit together. Projected custom-frame measurements use known border offsets, preserving real size/position feedback. Interactive gesture origins use physical frames to avoid treating an edge offset as a manual detach. Original regions and placements restore before journal entries are checkpointed. There is no reparenting or manager-driven minimization.

## Initial code versus planned code

Currently implemented: a WPF selected-window strip preview and M0 workbench; platform-neutral ordered columns, viewport scrolling, width cycling, minimum-width feedback, and reordering; serialized capture-before-move session orchestration with manual additions and pause/resume; native discovery/filtering, placement, DPI, identity markers, atomic journaling, session locking, and global shortcuts. Native fixture tooling exercises recovery across separate processes and the strip lifecycle.

The preview starts through explicit selection. Native events and a two-second fallback drive lifecycle reconciliation, with an unchecked per-session option to enroll newly opened eligible windows. Settings, tray controls, session lock/sleep handling, and full lifecycle compatibility verification remain planned work. Single-display fixtures and open Notepad/Chrome windows support direct off-screen physical placement at 100% scale. Broader adoption remains gated on editor/terminal/Explorer and higher DPI; see [the experiment record](m0-experiment.md) and [decision 0003](decisions/0003-opt-in-lifecycle-preview.md).

`ScrollingStrip` is the deterministic state/model boundary. `StripSession` receives an `IStripDesktop` adapter and recovery store, serializes commands with an asynchronous gate, and learns observed minimum widths through bounded layout retries. New windows are appended to the recovery journal before their first managed move. Closed windows are durably retired; temporarily ineligible windows retain a suspended column and their original recovery record. They rejoin when eligible, preserving width/order. Pause restores originals and retains the in-memory layout; resume captures fresh originals and reapplies that layout.

`NativeWindowEvents` owns its hooks and message pump on one background thread. The callback queues metadata without reading titles, moving windows, or writing recovery data. A concurrent input guard immediately defers layout during native move/size loops. The WPF dispatcher coalesces bursts for 80 ms and then invokes serialized reconciliation. Candidate inventories run on lifecycle events and fallback ticks; location events generated while applying a layout are suppressed. A listener failure pauses and restores the session.

Native move/size completion adopts a changed width, or detaches a moved window whose size is unchanged. Detachment checkpoints journal removal before releasing the marker, leaving the user's placement outside subsequent restoration. Explicit re-addition captures a fresh original. Deferred layout/focus commands survive a cancelled gesture. The fallback checks the window thread's move/size state before finishing a missed end notification; it does not snap a window underneath active input. These policies have deterministic coordinator coverage; full native gesture integration remains a manual verification gate.

The native diagnostic inventory reports exclusion reasons and saved placements without reading titles. An explicitly selected maximized window can be captured through the diagnostic-only path, journaled, temporarily restored using a non-activating normal show state, and restored to its original maximized placement. The normal capture method retains its maximized-window exclusion. This path is exercised only by the opt-in verification tool, not automatic management.

Original placement is stored in Windows workspace coordinates exactly as returned by `GetWindowPlacement` and restored through `SetWindowPlacement`. Physical visible-frame coordinates are measured through DWM, with invisible-border adjustment for managed positions. Unchanged monitor contexts restore exact placements; changed or missing contexts use the tested `RecoveryPlacement` centering/clamping policy and target workspace inset. Native restoration verifies placement, show state, and region before checkpointing. Version 2 journals store monitor/region/outer-frame metadata; version 1 remains readable without inventing missing original region data.

A random per-window property supplements process identity to detect a reused HWND within the same process. Markers survive manager termination and disappear when the target window is destroyed. The journal is flushed to disk before capture returns; subsequent saves replace the file in the same directory. Marker removal follows the successful restoration checkpoint. Windows/filesystem flush semantics do not constitute a guarantee against all hardware/power failures.

Replacement retries are bounded to four delays totaling 500 ms for sharing/lock errors and error 1175, with both original file names still present. There is no delete/recreate fallback. Other errors propagate to the recovery coordinator. In particular, partially completed replacements must not be blindly retried; see [Microsoft's replacement error semantics](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew).

## Technical references

- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [GUITHREADINFO move/size state](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-guithreadinfo)
- [GetWindowPlacement](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [High DPI desktop application development](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)
