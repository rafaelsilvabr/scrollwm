# Contributing

ScrollWM is an experimental multi-monitor scrolling window manager. Read [the MVP plan](docs/mvp-plan.md), [preview guide](docs/strip-preview.md), and [multi-monitor decision](docs/decisions/0004-multi-monitor-clipping.md) before starting a feature. Complete the compatibility gates before expanding automatic management.

## Setup and checks

Use Windows with the .NET 10 SDK and the commands in [README.md](README.md). Keep native desktop experiments opt-in and separate from unit tests. CI must never move windows on the runner's desktop.

Before opening a pull request:

```powershell
dotnet restore ScrollWM.slnx
dotnet build ScrollWM.slnx --configuration Release --no-restore
dotnet test ScrollWM.slnx --configuration Release --no-build --no-restore
```

Follow `.editorconfig`. Add meaningful unit coverage for changed layout/state rules and record manual verification for native behavior. Report unrun checks honestly, including the reason.

## Design and review

- Keep core rules independent of Windows APIs and UI.
- Preserve original window placements before moving windows.
- Prefer a small change tied to one backlog item; discuss scope changes through an issue first.
- Describe the user-visible behavior, validation, and remaining limitations in each pull request.
- Never include window titles, document contents, local config/recovery files, or credentials in fixtures or reports.
- Update the plan or an architecture decision when behavior or scope changes.

Use the GitHub bug and feature forms for reports and proposals. Include the Windows build, monitor arrangement, scaling, affected application versions, and restoration result for compatibility reports. Be respectful, explain disagreements with evidence, and do not publish another contributor's personal information.

Unless explicitly stated otherwise, contributions are provided under the repository's MIT license. No contributor license agreement is required.
