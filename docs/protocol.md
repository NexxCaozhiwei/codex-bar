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
      "version": "0.2.1"
    },
    "capabilities": {
      "experimentalApi": true,
      "optOutNotificationMethods": []
    }
  }
}
```

收到初始化响应后，客户端必须发送不带 `id` 和 `params` 的通知：

```json
{
  "method": "initialized"
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

`primary` 和 `secondary` 是协议槽位，不代表固定的 5h / 7d。Codex Bar 使用窗口自身的
`windowDurationMins` 生成标签，并隐藏值为 `null` 的槽位。响应还可能包含：

- `result.rateLimits.credits`
- `result.rateLimits.individualLimit`
- `result.rateLimits.rateLimitReachedType`
- `result.rateLimitResetCredits.availableCount`
- `account/rateLimits/updated` 稀疏更新通知

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

首次扫描后会监听 session 目录中新建、删除和重命名的 JSONL 文件，使新会话无需等待文件列表缓存到期即可被发现；无目录变更时仍复用 30 秒文件列表缓存。活动状态每 3 秒读取一次，额度读取使用设置中的独立刷新间隔。

活动上下文还会读取 `session_meta.payload.id` 和 `session_meta.payload.cwd`，并优先使用最近工具调用参数中的
`workdir` / `cwd` 作为实际项目目录。状态事件始终按 session
文件隔离后再归并；命令只从结构化的 `command`、`cmd`、`arguments` 或工具 `input` 字段分类。UI 不读取或展示
聊天正文、Prompt 或代码内容，未知命令的参数也不会进入展示摘要。

最多读取最近修改的 120 个文件，每个文件最多读取末尾 4 MB。它解析 `event_msg` 记录，其中 `payload.type == "token_count"` 且 `payload.rate_limits.limit_id == "codex"`。
