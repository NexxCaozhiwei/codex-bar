# Codex Bar Design

Codex Bar uses a Windows-native WPF shell with MVVM-style view models and small services for data access. The default UI is a borderless floating window positioned near the taskbar notification area. It does not inject into Explorer or modify the Windows taskbar.

## Components

- `MainWindow`: compact three-row status bar.
- `DetailsWindow`: quota and diagnostics view.
- `SettingsWindow`: user settings editor.
- `MainViewModel`: refresh orchestration and display properties.
- `QuotaService`: app-server first, jsonl fallback second.
- `CodexActivityDetector`: recent session event classification.
- `TrayService`: Windows Forms `NotifyIcon` integration.
- `StartupService`: current-user startup registration.
- `WindowDockingService`: taskbar-adjacent placement.

## Status Mapping

- `Idle`: red light.
- `Working` and `AutoReviewing`: green light.
- `Completed`: blue light.
- `WaitingForUser`: green/yellow text prompt.
- `Unknown` and `Error`: diagnostic text in details.

## Taskbar Strategy

The stable default is a floating tool window near the taskbar. `AppBarInterop` exists for future experimental docking, but it is not enabled by default because Windows Shell AppBars are edge-reserved application bars, not taskbar embeddings.
