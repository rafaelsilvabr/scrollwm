# 0003: Extend the developer preview with opt-in lifecycle management

Date: 2026-11-07. Status: accepted for experimental development. Extends [decision 0002](0002-staged-strip-preview.md).

## Context

The selected-window strip and recovery path work with disposable windows at 100% scale. M2 needs native lifecycle events, minimize/restore, and manual input policies before everyday use can be evaluated. The application and DPI compatibility matrix is still incomplete.

## Decision

Expose automatic enrollment as an unchecked, per-session option in the experimental preview. The user must select initial windows and start the session explicitly. Only windows first discovered after enablement may join automatically; existing unselected windows, including hidden windows, remain outside the strip. Resume establishes a new discovery baseline so windows opened while paused remain unselected. Manual additions remain available.

Run out-of-context WinEvent hooks on a dedicated message-pump thread. The callback publishes window metadata and a concurrent drag guard; geometry, recovery writes, and layout state remain serialized. Coalesce notifications for 80 ms, suppress placement-generated location notifications during application, and keep a two-second reconciliation fallback. Full candidate inventories are requested for lifecycle notifications and fallback reconciliation, rather than every location notification.

Retain a minimized or otherwise ineligible column's width and order, then reinsert it when eligible again. A completed manual resize adopts its width; a completed move without a size change detaches the column until explicitly re-added. Detachment durably removes its recovery entry before releasing its identity marker, leaving the user's placement intact on pause/exit. Re-adding captures that placement as the new original. A cancelled drag applies any commands deferred while input was active.

## Consequences

This relaxes decision 0002's prohibition on automatic enrollment only for the explicitly enabled developer preview. It does not satisfy the compatibility gate for default automatic management or public release. One display and 100% scaling remain required. Two-screen operation remains a required follow-up; removing its guard is insufficient.

Unit tests cover the lifecycle and journal rules. A separate marked-fixture tool verifies native event delivery without moving user application windows. Full event-to-layout and interactive drag/resize checks remain pending on one display, as do sleep/lock behavior and the broader application/DPI matrix. Settings and tray controls remain M3 work.
