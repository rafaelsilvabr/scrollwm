# Changelog

## Unreleased

- Prepare the source repository for open source publication with a clearer README, three editable SVG illustrations, updated contribution guidance, dependency update configuration, and a GitHub publication guide.

- Keep partial edge windows visible inside their monitor at their existing widths, beneath fully visible managed windows. Focus or click an exposed edge to reveal it. Park only entirely off-viewport columns.
- Track physical gesture origins for shifted edge windows to avoid detaching them after a cancelled drag. Guard incompatible always-on-top ordering without changing topmost state.
- Verify 100 core tests, 15 native fixture checks, 36 native Chrome/VS Code/Xournal++ placement checks, and exposed-edge clicking with two disposable windows. All measured restorations leave zero pending entries.

- Park partly visible columns as well as fully hidden ones. Verify full outer-frame containment and disable partial edge previews so compositor output cannot occupy neighboring screens. Use an 8 pixel multi-monitor margin for resize borders.
- Include gaps in strip width presets so two halves and three thirds fit as whole windows. Preserve existing widths during insertion.
- Measure parked custom-frame windows from their outer bounds and known borders, handling VS Code's off-screen DWM measurement change. Verify 99 core tests, 14 native fixture checks, and 36 native Chrome/VS Code/Xournal++ checks with exact restoration and no pending recovery.

- Park fully hidden columns beyond all connected screens so long strips cannot place those windows on a neighboring monitor. Keep logical strip positions and widths, reveal before focus, and restore the original capture on pause/exit. Add parking and focus/isolation regressions.
- Verify the parking correction with 92 passing core tests, all 13 updated native two-monitor fixture checks, and ten native Chrome position checks. Fixture and Chrome restoration leave zero pending entries.

- Accept app-owned window shapes contained within the monitor clip instead of requiring exact rectangular equality. Continue rejecting overflow and unexpectedly hidden windows. Name the application when clipping fails, and add an explicit-selection monitor diagnostic with journaled restoration.

- Add independent strips per monitor, monitor selection, and focused-window transfer with Ctrl+Alt+M. Preserve destination widths and the original recovery capture across transfers.
- Clip managed windows to their owning monitor to prevent off-viewport drawing on adjacent screens. Persist original window regions and display context in version 2 journals; retain version 1 recovery support.
- Scale margins, gaps, presets, and custom transfers per monitor. Pause and restore after topology changes, with a visible fallback for missing/reconfigured monitors.
- Add multi-monitor isolation, scaling, transfer, and recovery tests plus a marked-fixture verifier for native clipping and fresh-process crash recovery.
- Verify 13 native checks on two 100% monitors and 70 core tests. Check monitor selection, button/shortcut transfer, empty state, pause, and minimum-size UI with disposable windows. Nine persistence tests passed in staging; Windows Application Control blocked their final default-output rerun.
- Compact the WPF interface within the existing window and two-pane layout. Group controls with their lists, show effective state and widths, replace raw handles with app labels, and retain diagnostics in tooltips and Position probes.
- Add native vector icons and an SVG preview. Verify ready, running, paused, minimum-size, expanded-help, and diagnostic layouts; exercise additions, width, navigation, reorder, restoration, resume, and emergency restore with disposable windows.
- Add an Add open windows button to discover and enroll eligible windows outside the running strip in one step. Refresh candidates after additions and skip already managed selections.
- Reduce the strip's outer screen margin from 12 to 6 pixels, retaining 12 pixel gaps between windows.
- Select all eligible open windows by default on launch and refresh; omit already managed columns from refresh while running so Add selected can enroll the remaining windows.
- Show a persistent activation banner with display/scaling/recovery requirements and explicit selection/start instructions. Disable strip activation when requirements are unmet; preserve the two-display explanation after startup.
- Add native lifecycle hooks, coalesced event reconciliation, and opt-in automatic enrollment of newly opened eligible windows, preserving existing unselected windows.
- Preserve suspended columns across minimize/restore; adopt manual resize widths and detach manually moved windows without undoing their placement on exit. Explicit re-addition captures a new original placement.
- Defer layout/focus commands during active input, recover missed gesture-end notifications, and durably retire closed-window records before handle reuse.
- Add a disposable-window event verification command; all six native event checks and 62 automated tests pass. Full event-to-layout and interactive gesture verification remains pending on one display.
- Add a PowerShell launcher that runs the existing app directly and selects the project-local SDK when a build is needed; clarify runtime-only system installations in the setup guides.
- Implement the selected-window scrolling strip preview: ordered columns, minimal scrolling, preserved widths, keyboard navigation/reordering, width cycling, and manual additions.
- Add serialized journal-before-move orchestration, minimum-width feedback, pause/resume, closed/ineligible-window removal, display/DPI guards, and all-or-none shortcut registration.
- Add five-window strip verification and 18 additional automated tests; verify a clean Release build, 47 passing tests, and all nine native strip checks. Interactive shortcut/close checks remain pending after a computer-use activation failure.
- Handle destroyed windows returning DPI zero without misclassifying closure as a scaling change.
- Add selected-window diagnostics with full identity revalidation, exclusion reasons, actual foreground checks, width presets, and restoration measurements.
- Add an explicit maximized-window diagnostic option while retaining the default manager exclusion.
- Verify the open Notepad/Chrome windows at 100% scale: all 18 normal geometry targets match, minimum widths are respected, and original placements/show states restore exactly. Verify fresh-process Notepad restoration after probe-worker termination.
- Confirm two-screen support as a required follow-up milestone, with independent strips, mixed DPI, transfers, and topology recovery.
- Retry transient journal replacement locks with a bounded delay; add real-file tests for brief and persistent locks after a native recovery check exposed a checkpoint failure.
- Implement the opt-in native experiment: window discovery, placement capture, edge probes, reveal/focus, restoration, and an emergency shortcut.
- Add atomic recovery journaling, process/window identity checks, single-session locking, and multiple-display movement refusal.
- Add disposable fixture windows, a native verification/recovery tool, and journal/recovery failure-path coverage.
- Verify all 15 normal single-display fixture probes at 100% scale, off-screen cross-process restoration after forced worker termination, and emergency restoration through the M0 workbench. Remaining application and higher-DPI coverage is still open.
- Draft the single-display Windows MVP, implementation milestones, and manual verification gates.
- Add app/core/native project boundaries, a WPF welcome shell, width-preset rules, and test source.
- Add GitHub CI and contribution/reporting templates for later publication.

The selected-window strip is experimental. Full automatic management and a public release are not available yet.
