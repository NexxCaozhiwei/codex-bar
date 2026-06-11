# 故障排查

## 缺少 GitHub CLI

安装 GitHub CLI 并完成认证：

```powershell
winget install --id GitHub.cli
gh auth login
gh auth status
```

## 缺少 .NET SDK

请安装 .NET 8 SDK，不要只安装 Runtime：

```powershell
winget install --id Microsoft.DotNet.SDK.8
dotnet --info
```

## 缺少 Codex CLI

安装 Codex CLI 并验证：

```powershell
where codex
codex --version
```

如果 Codex 安装在自定义位置，请在 Codex Bar 设置页中指定路径。

## 没有额度数据

请检查：

- Codex CLI 已安装。
- Codex 已登录。
- `codex app-server --listen stdio://` 可以启动。
- 本地 session 日志存在于 `%USERPROFILE%\.codex\sessions`。

## 设置文件损坏

如果 `settings.json` 损坏，Codex Bar 会把它重命名为 `.bad-*` 备份，并重新创建默认设置。
