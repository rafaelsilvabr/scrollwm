# 0002: Develop the strip core with an opt-in native preview

Date: 2026-11-07. Status: accepted for experimental development.

The opt-in lifecycle extension in [decision 0003](0003-opt-in-lifecycle-preview.md) supersedes the enrollment/event restrictions below for the developer preview. The compatibility and release gates remain open.

## Context

M0 has verified real-window edge placement, minimum widths, focus, and restoration with fixtures and the open Notepad/Chrome windows at 100% scale. Editor, terminal, Explorer, higher DPI, and topology coverage remain open. Implementation is continuing toward a usable strip while those compatibility checks remain visible.

## Decision

Proceed with M1's platform-neutral ordered strip and a selected-window native preview using the demonstrated single-display physical-placement approach. This revises the original strictly sequential M0/M1 order for experimental development. The M0 compatibility gate still applies to automatic management and public-release readiness.

The preview requires explicit selection and enablement, one active display, and 100% display scaling. It must persist originals before moves, serialize commands, learn app minimum widths without shrinking neighbors, and restore/pause after placement or display failures. No automatic enrollment, hiding, reparenting, or manager-driven minimization is added.

Use a low-rate reconciliation pass to remove closed/ineligible windows and reveal externally focused managed windows. Full native lifecycle events, manual drag/resize policy, automatic enrollment, settings, and tray controls remain M2/M3 work.

## Consequences

Developers can exercise the actual strip and its recovery path now without marking untested compatibility as complete. Higher DPI and two-screen behavior must be implemented and verified before their preview restrictions are relaxed. Existing M0 probe tools remain available in a separate tab.
