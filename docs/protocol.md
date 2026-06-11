# Codex 数据协议

## app-server

Codex Bar 启动：

```powershell
codex app-server --listen stdio://
```

它通过 stdin/stdout 发送换行分隔的 JSON-RPC-like 消息。当前 Codex app-server 生成的 schema 在线上消息中不包含标准 `jsonrpc: "2.0"` 字段。

初始化：

```json
{
  "id": 1,
  "method": "initialize",
  "params": {
    "clientInfo": {
      "name": "codex-bar",
      "title": "Codex Bar",
      "version": "0.1.3"
    },
    "capabilities": {
      "experimentalApi": true,
      "optOutNotificationMethods": []
    }
  }
}
```

读取额度：

```json
{
  "id": 2,
  "method": "account/rateLimits/read"
}
```

生成协议中 `params: undefined` 的方法需要省略 `params`，不要发送 `null`。

支持的额度结构：

- `result.rateLimitsByLimitId.codex.primary`
- `result.rateLimitsByLimitId.codex.secondary`
- `result.rateLimits.primary`
- `result.rateLimits.secondary`

支持的字段别名：

- `windowDurationMins` / `window_minutes`
- `usedPercent` / `used_percent`
- `resetsAt` / `resets_at`
- `planType` / `plan_type`
- `limitId` / `limit_id`

当前 app-server 版本可能把 `resetsAt` 编码成 Unix 时间戳数字。Codex Bar 同时支持 Unix 时间戳和日期时间字符串。

## jsonl 回退

Codex Bar 扫描：

```text
%USERPROFILE%\.codex\sessions\**\*.jsonl
```

最多读取最近修改的 120 个文件，每个文件最多读取末尾 4 MB。它解析 `event_msg` 记录，其中 `payload.type == "token_count"` 且 `payload.rate_limits.limit_id == "codex"`。
