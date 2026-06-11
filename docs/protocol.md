# Codex Data Protocol

## app-server

Codex Bar starts:

```powershell
codex app-server --listen stdio://
```

It sends newline-delimited JSON-RPC messages over stdin/stdout.

Initialize:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "clientInfo": {
      "name": "codex-bar",
      "title": "Codex Bar",
      "version": "0.1.0"
    },
    "capabilities": {
      "experimentalApi": true,
      "optOutNotificationMethods": []
    }
  }
}
```

Quota read:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "account/rateLimits/read",
  "params": null
}
```

Supported quota shapes:

- `result.rateLimitsByLimitId.codex.primary`
- `result.rateLimitsByLimitId.codex.secondary`
- `result.rateLimits.primary`
- `result.rateLimits.secondary`

Supported field aliases:

- `windowDurationMins` / `window_minutes`
- `usedPercent` / `used_percent`
- `resetsAt` / `resets_at`
- `planType` / `plan_type`
- `limitId` / `limit_id`

## jsonl fallback

Codex Bar scans:

```text
%USERPROFILE%\.codex\sessions\**\*.jsonl
```

It reads the newest 120 files at most and up to the last 4 MB per file. It parses `event_msg` records with `payload.type == "token_count"` and `payload.rate_limits.limit_id == "codex"`.
