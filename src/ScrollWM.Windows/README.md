# Windows integration boundary

This project implements the native boundary for the M0 experiment and M1 selected-window preview. Usage/results are in [the experiment guide](../../docs/m0-experiment.md) and [strip preview guide](../../docs/strip-preview.md).

Implemented:

- Top-level window discovery/filtering without reading window titles.
- Process identity and per-window recovery markers to guard against handle reuse.
- Capture/apply/restore placement, visible frame measurement, and focus requests.
- Work-area, DPI, and display-count information; movement restricted to one active display.
- Atomic JSON recovery storage and single-session manager locking.
- Emergency shortcut registration with a visible failure if unavailable.
- All-or-none strip shortcut registration and an `IStripDesktop` adapter for the recovery-aware coordinator.
- Diagnostic inspection with exclusion reasons and an explicit maximized-window probe path that preserves original show state; default management still excludes maximized windows.

Full lifecycle event hooks, batched placement, and automatic enrollment remain planned work. The preview currently uses low-rate reconciliation of explicitly selected windows.

Keep native handles, P/Invoke declarations, and Windows-specific error handling here. Do not implement layout policy or tray UI in this project. Add native desktop experiments only as explicit opt-in tooling; automated unit tests must not rearrange the desktop.
