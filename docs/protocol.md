# Codex Data Protocol

## app-server

Codex Bar starts:

```powershell
codex app-server --listen stdio://
```

It sends newline-delimited JSON-RPC-like messages over stdin/stdout. Current Codex app-server schema does not include the standard `jsonrpc: "2.0"` field on the wire.

Initialize:

```json
{
  "id": 1,
  "method": "initialize",
  "params": {
    "clientInfo": {
      "name": "codex-bar",
      "title": "Codex Bar",
      "version": "0.1.1"
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
  "id": 2,
  "method": "account/rateLimits/read"
}
```

For methods whose generated protocol type has `params: undefined`, Codex Bar omits `params` instead of sending `null`.

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

Current app-server builds may encode `resetsAt` as a Unix timestamp number. Codex Bar accepts both Unix timestamp numbers and date-time strings.

## jsonl fallback

Codex Bar scans:

```text
%USERPROFILE%\.codex\sessions\**\*.jsonl
```

It reads the newest 120 files at most and up to the last 4 MB per file. It parses `event_msg` records with `payload.type == "token_count"` and `payload.rate_limits.limit_id == "codex"`.
