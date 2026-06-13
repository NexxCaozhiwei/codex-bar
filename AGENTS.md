# AGENTS.md

This file gives coding agents project-specific guidance for working in this repository.

## Project

Codex Bar is an unofficial Windows 11 WPF utility for showing local Codex activity status and quota usage near the taskbar notification area.

- Main app: `src/CodexBar`
- Tests: `tests/CodexBar.Tests`
- Docs: `docs`
- Release workflows: `.github/workflows`
- Build target: Windows x64, .NET 8, WPF

The app is local-first. Do not add telemetry, upload logs, or send session contents to external services.

## Important Commands

Run from the repository root.

```powershell
dotnet restore
dotnet build CodexBar.sln -c Release
dotnet test CodexBar.sln -c Release --no-build
```

Publish a local Windows x64 package:

```powershell
dotnet publish src/CodexBar/CodexBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/publish/win-x64
Compress-Archive -Path artifacts/publish/win-x64/* -DestinationPath artifacts/CodexBar-win-x64.zip -Force
```

Before republishing locally, stop any running `CodexBar.exe` from this workspace so `artifacts/publish/win-x64/CodexBar.exe` is not locked.

## Release Process

GitHub Releases are created by pushing a tag matching `v*`.

1. Update version values in:
   - `src/CodexBar/CodexBar.csproj`
   - `src/CodexBar/Services/CodexAppServerClient.cs`
   - `docs/protocol.md`
2. Build and test locally.
3. Commit intentionally.
4. Push `main`.
5. Create and push the version tag.

Example:

```powershell
git push origin main
git tag v0.1.6
git push origin v0.1.6
```

Do not commit `artifacts/` outputs.

## Implementation Notes

### UI

- UI is Chinese-first. Keep user-facing text in Chinese unless there is a protocol, product-name, or technical reason to keep English.
- Main UI is intentionally compact. If changing `MainWindow.xaml`, verify both `5h` and `7d` rows are visible at high DPI.
- The main window is shown first, then placement is applied. Do not move taskbar docking back into the window constructor, because DPI information may not be available before `Show()`.
- The tray icon uses the application icon extracted from the running executable. Keep `src/CodexBar/Resources/CodexBar.ico` wired through `ApplicationIcon`.

### Codex Activity Detection

`CodexActivityDetector` must be lifecycle-based, not raw substring matching.

- Active events within 60 seconds map to `Working`.
- Completion events within 30 seconds map to `Completed`.
- Completion events older than 30 seconds map to `Idle`.
- Waiting-for-user or approval events within 5 minutes map to `WaitingForUser`; older ones map to `Idle`.
- Old active events without later completion should prefer `Idle` with a detail like `最近未检测到新的 Codex 活动。`.
- Missing timestamps must not become `DateTimeOffset.Now`. Prefer top-level `timestamp`, then nested completion/creation fields, then `CodexSessionLogEntry.FileLastWriteTimeUtc`; if no time is available, return `Unknown`.
- Parse JSONL with `System.Text.Json`. Do not reintroduce broad line-level `Contains` checks for state decisions.

Keep the activity tests in `tests/CodexBar.Tests/CodexActivityDetectorTests.cs` updated when changing these rules.

### Quota Reading

- Prefer `codex app-server --listen stdio://` and `account/rateLimits/read`.
- Fall back to local `%USERPROFILE%\.codex\sessions\**\*.jsonl`.
- Keep parser support for both camelCase and snake_case fields.
- Do not bypass Codex quota rules or access controls.

### Settings and Startup

- Settings are stored in `%APPDATA%\CodexBar\settings.json`.
- Startup uses the current-user Run key:
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Do not require administrator privileges.

## Git Hygiene

- The working tree may contain user-created untracked files. Do not add unrelated files accidentally.
- In this workspace, `INSTALL.md` may be untracked; do not include it unless the user explicitly asks.
- Avoid broad refactors when fixing narrow behavior.
- Use focused tests for service logic changes.

## Verification Checklist

For most code changes:

```powershell
dotnet build CodexBar.sln -c Release
dotnet test CodexBar.sln -c Release --no-build
```

For UI, icon, startup, or release changes, also run a local publish and launch `artifacts/publish/win-x64/CodexBar.exe` for a smoke test.
