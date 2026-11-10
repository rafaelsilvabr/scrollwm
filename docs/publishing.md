# Publishing ScrollWM on GitHub

This source tree includes an MIT license, contribution and security policies, issue forms, a pull request template, Windows build/test CI, Dependabot configuration, and local SVG README artwork. Build outputs, local tooling, recovery journals, and native diagnostic reports are ignored by Git.

The application is a developer preview. Publishing the source does not establish a stable release or complete the compatibility matrix.

## Review the first commit

From the repository root, review the files and run the documented checks:

```powershell
git status --short
dotnet restore ScrollWM.slnx
dotnet build ScrollWM.slnx --configuration Release --no-restore
dotnet test ScrollWM.slnx --configuration Release --no-build --no-restore
git add .
git diff --cached --stat
git diff --cached --check
```

Use `.\.tools\dotnet\dotnet.exe` instead of `dotnet` if using a project-local SDK. Confirm that no private data, local paths in diagnostic reports, binaries, credentials, or recovery journals are staged. Confirm the working name and existing MIT license before the first public release. Set your Git author identity if needed, then create the initial commit:

```powershell
git commit -m "Prepare ScrollWM developer preview for open source"
```

## Create the remote

Choose the final repository name on your GitHub account. With GitHub CLI installed and authenticated, this example creates a **public** repository and uploads the committed source:

```powershell
gh repo create scrollwm --public --source=. --remote=origin --push --description "Experimental scrolling window manager for Windows 11. Keep useful window widths and navigate independent strips per monitor."
```

Alternatively, create an empty repository in GitHub without an initial README or license, then follow its instructions to add `origin` and push `main`. This guide does not create or publish a remote automatically.

## Configure GitHub

- Enable Issues and private vulnerability reporting under **Settings → Code security**. If private reporting is unavailable, add a working private contact to [SECURITY.md](../SECURITY.md) before publication.
- Add topics such as `windows`, `window-manager`, `tiling-window-manager`, `csharp`, `wpf`, and `dotnet`.
- Require the **Build and test / build** check on `main` through a branch rule or ruleset. Confirm the first CI run passes before requiring it.
- Verify the rendered README and the bug / feature forms. All artwork uses relative paths, so it also works in forks.

## Before a binary release

Complete the [manual test plan](manual-test-plan.md) and outstanding [multi-monitor compatibility checks](multi-monitor-plan.md). Keep release notes explicit about tested configurations and remaining limits. Package only the intended app output with the license, changelog, usage instructions, and runtime requirements; inspect the archive for private files. Signing, installation, automatic updates, and binary release automation are outside the current source-publication setup.
