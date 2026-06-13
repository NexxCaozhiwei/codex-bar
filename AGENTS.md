# AGENTS.md

本文件给 Codex / AI 代理提供本仓库的最小必要工作规则。目标是让代理能稳定完成代码修改、验证和提交，同时避免遗漏未跟踪文件、误触发发布或破坏现有 UI 约束。

## 项目概况

Codex Bar 是 Windows 11 上的 WPF / .NET 8 小工具，用于在任务栏附近显示 Codex 状态、5h 额度和 7d 额度。

- 主程序：`src/CodexBar`
- 测试：`tests/CodexBar.Tests`
- 文档：`docs`
- CI / 发布：`.github/workflows`
- 本地发布产物：`artifacts/`，不要提交

面向用户的 UI 文案和文档默认使用中文。

## 默认工作方式

除非用户明确要求只分析、不改代码，代理应直接完成：

1. 阅读相关代码和文档。
2. 做最小必要修改。
3. 按改动范围运行验证。
4. 提交代码。

默认不要推送、不要打 tag、不要创建 GitHub Release。只有用户明确要求“推送”“发行”“发布”“打包并发行”时，才执行远端推送和版本发布。

## 未跟踪文件处理

提交前必须检查：

```powershell
git status --short
```

遇到未跟踪文件时，不要忽略。应主动整理：

- 明显属于当前任务的源码、文档、资源文件，应加入提交。
- 明显是构建产物、截图、临时文件、缓存文件，应保持不提交，必要时说明。
- 内容已经过时或用途不明的文件，应先查看内容，再决定是加入、保留未跟踪，还是向用户说明。
- 不要把 `artifacts/` 发布产物加入仓库。

## 常用命令

构建和测试：

```powershell
dotnet build CodexBar.sln -c Release
dotnet test CodexBar.sln -c Release --no-build
```

本地发布包：

```powershell
dotnet publish src/CodexBar/CodexBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/publish/win-x64
Compress-Archive -Path artifacts/publish/win-x64/* -DestinationPath artifacts/CodexBar-win-x64.zip -Force
```

重新 publish 前，先停止本仓库路径下正在运行的 `CodexBar.exe`，否则 `artifacts/publish/win-x64/CodexBar.exe` 可能被锁定。

## 验证要求

按改动范围选择验证：

- 普通代码改动：运行 `dotnet build` 和 `dotnet test`。
- UI、图标、启动行为、设置页、发布流程改动：额外执行本地 publish，并启动发布版 exe 做冒烟验证。
- 纯文档改动：可只做格式和链接检查，不必强行构建。

验证失败时，先修复再提交。最终回复中说明已运行的验证命令。

## 发布规则

只有用户明确要求发行或发布时，才执行以下流程：

1. 将版本号升级到下一个版本。
2. 同步修改：
   - `src/CodexBar/CodexBar.csproj`
   - `src/CodexBar/Services/CodexAppServerClient.cs`
   - `docs/protocol.md`
3. 构建、测试、本地 publish、启动冒烟。
4. 提交版本变更。
5. 推送 `main`。
6. 创建并推送 tag，例如：

```powershell
git tag v0.1.7
git push origin v0.1.7
```

GitHub Release 由 `.github/workflows/release.yml` 在 tag push 后自动创建。

## UI 约束

- UI 中文优先。
- 主 UI 保持紧凑，适合任务栏附近悬浮显示。
- 不要把主界面改成大窗口、营销页或装饰性布局。
- 修改 `MainWindow.xaml` 后，要确认 `5h` 和 `7d` 两行都完整可见。
- 主窗口需要先 `Show()` 再应用位置，避免高 DPI 下定位错误。
- 设置页新增控件时，要确认窗口高度足够，底部按钮和语言设置不可被裁切。

## 状态判定规则

`CodexActivityDetector` 必须基于 JSONL 生命周期事件判断，不要回退到整行字符串粗暴 `Contains`。

核心规则：

- active 事件 60 秒内显示 `Working`。
- `task_complete`、`turn_completed`、`completed` 事件 30 秒内显示 `Completed`。
- 完成事件超过 30 秒后显示 `Idle`。
- `request_user_input`、`approval`、`permission`、`waiting_for_user` 等事件 5 分钟内显示 `WaitingForUser`，超过后显示 `Idle`。
- active 事件超过 60 秒且没有后续完成事件时，优先显示 `Idle`，详情说明“最近未检测到新的 Codex 活动”。
- timestamp 解析顺序：顶层 `timestamp`，再解析 payload / item 的完成或创建时间，再用 session 文件 `LastWriteTimeUtc`，仍无法判断时返回 `Unknown`。
- 错误、网络异常、断线、超时等本地日志字段应映射到 `Error`。

修改状态规则时，同步维护 `tests/CodexBar.Tests/CodexActivityDetectorTests.cs`。

## 额度读取规则

- 优先使用 `codex app-server --listen stdio://` 读取 `account/rateLimits/read`。
- app-server 不可用时，回退扫描 `%USERPROFILE%\.codex\sessions\**\*.jsonl`。
- 解析器需要兼容 camelCase 和 snake_case 字段。
- 不要绕过 Codex 的额度规则、登录状态或访问控制。

## Git 规则

- 提交信息使用简短英文祈使句。
- 一个提交只做一类事情；文档、版本升级、功能修复可以分开提交。
- 不要提交 `artifacts/`、临时截图、缓存和本地机器专属文件。
- 推送和发布只在用户明确要求时执行。
