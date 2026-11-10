# 0001: C#/.NET 10 with WPF and a Win32 adapter layer

Date: 2026-11-07. Status: proposed for the MVP.

## Context

The product manages existing desktop application windows. Its central requirement is access to Windows window events, placement, focus, hotkeys, and restoration. The first target is Windows 11 x64.

## Decision

Use C# on .NET 10 with WPF for the small settings/lifecycle shell. Keep deterministic layout rules in a plain .NET library and put native integration in a separate Windows-targeted library. Use direct Win32 interop where needed; consider generated interop only when native coverage justifies another dependency.

Use xUnit v3 with the VSTest adapter for the initial core test suite. The solution uses `.slnx`, supported by the selected SDK. Publish a self-contained x64 folder/ZIP for the first alpha.

## Consequences

WPF is Windows-only, which matches the initial scope. Native behavior still needs experimental verification: selecting WPF does not solve clipping, focus restrictions, elevated windows, or app minimum widths. Settings UI runs on the WPF dispatcher; event handling must not stall it.

Cross-platform shells and web-rendered UI do not reduce the core Win32 integration work enough to justify them for this MVP. Revisit the shell if there is a concrete cross-platform requirement.

The initial repository license is MIT and the name ScrollWM is provisional. Reconfirm both before publication. No third-party application source or visual assets are copied into the scaffold.

## References

- [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET SDK version selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)
- [xUnit v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started)
