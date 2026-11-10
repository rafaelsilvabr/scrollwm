# 0004: Keep independent monitor strips and recoverable window clips

Date: 2026-11-09. Status: accepted for the experimental preview. Extends decisions 0002 and 0003 in response to the request to implement multiple monitors now.

## Decision

Keep one `ScrollingStrip` per monitor under a shared `StripSession` command gate and recovery journal. Each strip owns its column order, widths, focus, and offset. Monitor ownership is stored separately from current window rectangles: an off-viewport rectangle can physically overlap another monitor without changing ownership. Layout changes apply only to dirty strips.

Read monitor bounds, work areas, primary status, and scale from Windows. Use monitor device interface IDs when available, with a GDI device-name fallback for virtual providers. Scale the 6 pixel margin and 12 pixel gap from 96 DPI to the target monitor's physical pixels. Resolve transferred presets against the destination viewport; scale custom widths and learned minimums by the DPI ratio.

Keep edge columns visible by shifting their full frames inside the owning viewport at unchanged sizes. Place them beneath fully visible managed columns on the same monitor, using an existing managed anchor without activation or promotion above unrelated apps. Guard incompatible topmost groups rather than changing an uncaptured topmost state. This overlap behavior was explicitly selected by the user after hiding partial columns made neighbors disappear. Only entirely off-viewport columns park beyond all connected screens. Verify isolation from outer frames and edge ordering from native Z order. Keep normal show state, taskbar entries, identity, logical strip positions, and widths. Projected measurements preserve real size/position feedback; physical gesture origins prevent accidental detach on a cancelled edge drag. Focus reveals an edge fully before activation. Reject oversized windows instead of parking a focused window indefinitely. There is no reparenting or manager-driven minimization.

Capture the original region, outer bounds, monitor context, and placement before movement. Version 2 journals persist this metadata; version 1 journals remain readable and do not trigger restoration of region data they never captured. Restore the placement and original region before checkpointing success and releasing the identity marker. Detaching restores presentation without undoing the user's placement before removing its recovery entry.

Keep minimized managed windows clipped to an empty region until they return to the strip. Other ineligible windows have their original presentation restored while suspended. Stop and restore if an application rejects clipping or repeatedly replaces its clip; do not fight the app in an event loop.

Moving between monitors transfers a column. Moving within one monitor retains the existing detach policy. Explicit transfer uses the same durable original capture. Global commands follow newly focused managed windows; choosing a monitor in the UI is respected while foreground focus is unchanged.

On a monitor count, bounds, work-area, scale, or primary change, pause and restore all captured windows. Exact original placement is retained when its display context is unchanged. Otherwise center the original logical size, clamped to the visible work area, on the matching monitor or primary monitor if it is absent. Retain any entry Windows cannot restore or verify. Start a new strip after topology changes.

## Verification and limits

Deterministic tests cover negative origins, independent updates, mixed DPI calculations, enrollment, transfers, suspension, topology failure, and missing-display placement. Native fixture verification checks geometry, actual GDI region bounds, original shape restoration, and fresh-process recovery on the connected displays. See [the monitor record](../multi-monitor-plan.md).

Mixed DPI, real hotplug, custom application behavior, and native cross-monitor dragging still require the broader compatibility matrix. Monitor interface IDs and GDI names are not a claim of universal hardware identity across docks, remote sessions, and driver replacements. A manager that is forcibly terminated cannot remove live clips until recovery runs.

## References

- [SetWindowPos: relative ordering and SWP_NOACTIVATE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)

- [SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)
- [GetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowrgn)
- [ExtCreateRegion](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-extcreateregion)
- [EnumDisplayDevices](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw)
- [GetScaleFactorForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getscalefactorformonitor)
