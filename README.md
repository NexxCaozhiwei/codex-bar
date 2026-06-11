# Codex Bar

Codex Bar is an unofficial Windows 11 desktop companion for OpenAI Codex users. It shows Codex activity and remaining quota in a small taskbar-adjacent status bar without injecting into or modifying the Windows taskbar.

```text
Codex  red green blue  Working
5h     [======----]     68% left
7d     [========= ]     91% left
```

## Features

- Small WPF floating bar near the Windows notification area.
- System tray icon with show, hide, refresh, settings, startup, top-most, lock-position, and exit actions.
- 5-hour and 7-day quota remaining bars.
- Codex status detection from local session jsonl events.
- Preferred quota source: `codex app-server --listen stdio://`.
- Fallback quota source: `%USERPROFILE%\.codex\sessions\**\*.jsonl`.
- User-level startup through `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Settings stored in `%APPDATA%\CodexBar\settings.json`.
- No administrator privileges required.

## Install

Install the .NET 8 SDK on Windows 11 x64, then build or download a release artifact.

```powershell
dotnet restore
dotnet build -c Release
dotnet publish src/CodexBar/CodexBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/publish/win-x64
```

Run:

```powershell
artifacts\publish\win-x64\CodexBar.exe
```

## Usage

- Left-click the bar to open details.
- Right-click the bar or tray icon for actions.
- Drag the bar to reposition it unless position lock is enabled.
- Use Settings to set a custom Codex command path, refresh interval, top-most behavior, startup behavior, and language preference.

## Codex CLI

Codex Bar first tries the path configured in settings, then checks `where codex`, `where codex.cmd`, `where codex.exe`, and common npm global locations. If Codex is not found, the UI shows a clear error and falls back to local session logs when possible.

## Startup

Enable startup from the tray menu or Settings. Codex Bar writes only the current user's `Run` registry key and does not require admin rights.

## Exit

Use the tray menu or right-click menu and choose `Exit`.

## Known Limits

- Codex local `app-server` APIs are experimental and may change.
- AppBar docking is included only as an interop surface; the default mode is a stable taskbar-adjacent floating window.
- Full-screen application detection is intentionally conservative in this initial version.
- If both app-server and local jsonl logs are unavailable, quota data is shown as unavailable.

## Privacy

Codex Bar reads only local Codex app-server responses and local Codex session jsonl files. It does not upload user code, does not upload Codex logs, and does not collect telemetry.

## Unofficial Notice

This project is unofficial and is not provided by OpenAI. Codex local app-server interfaces may change in future Codex releases.

## License

MIT. See [LICENSE](LICENSE).

## Acknowledgements

This project references the product idea of [Tongzh-SEU/Codex-Signal-Glance](https://github.com/Tongzh-SEU/Codex-Signal-Glance), a MIT-licensed macOS Codex quota and activity glance tool. Codex Bar is a Windows-native rewrite and does not copy Swift/macOS implementation code.
