# Troubleshooting

## GitHub CLI is missing

Install GitHub CLI, then authenticate:

```powershell
winget install --id GitHub.cli
gh auth login
gh auth status
```

## .NET SDK is missing

Install the .NET 8 SDK, not only the runtime:

```powershell
winget install --id Microsoft.DotNet.SDK.8
dotnet --info
```

## Codex CLI is missing

Install Codex CLI and verify:

```powershell
where codex
codex --version
```

If Codex is installed in a custom location, set the path in Codex Bar Settings.

## No quota data

Check:

- Codex CLI is installed.
- Codex is logged in.
- `codex app-server --listen stdio://` can start.
- Local session logs exist under `%USERPROFILE%\.codex\sessions`.

## Corrupted settings

Codex Bar renames corrupted `settings.json` to a `.bad-*` backup and recreates defaults.
