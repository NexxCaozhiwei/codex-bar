# Codex Bar 设计

Codex Bar 使用 Windows 原生 WPF 外壳，并用接近 MVVM 的方式组织视图模型和服务。默认界面是一个贴近任务栏通知区域的无边框悬浮窗口。它不会注入 Explorer，也不会修改 Windows 任务栏。

## 组件

- `MainWindow`：三行紧凑状态条。
- `DetailsWindow`：额度和诊断详情。
- `SettingsWindow`：用户设置编辑。
- `MainViewModel`：刷新调度和界面显示属性。
- `QuotaService`：优先 app-server，失败后回退 jsonl。
- `CodexActivityDetector`：把最近 session JSONL 生命周期事件归一化为活动事件。
- `CodexActivityReducer`：按时间、优先级、粘性状态和超时窗口归并活动事件。
- `ActionClassifier`：把内部状态和结构化命令归纳为面向用户的当前动作，并生成脱敏的短命令摘要。
- `ProjectResolver`：从 session `cwd` 解析 Git 根目录和项目名，按 cwd 缓存成功与失败结果。
- `ActivityNotificationService`：只根据稳定状态跃迁生成关键事件通知。
- `TrayService`：Windows Forms `NotifyIcon` 托盘集成。
- `StartupService`：当前用户开机启动注册。
- `WindowDockingService`：任务栏附近定位。

额度窗口按 `windowDurationMins` 动态生成。`primary` / `secondary` 只视为协议槽位；某个槽位为
`null` 时不创建占位行，也不会把另一个窗口复制成缺失的 5h 或 7d 数据。

## 状态模型

- 灯位顺序固定为绿灯、蓝灯、红灯。
- `Idle` 和 `Completed`：绿灯。
- `Thinking`、`Editing`、`RunningCommand`、`RunningTests` 和 `Reviewing`：蓝灯。
- `WaitingApproval` 和 `WaitingUser`：蓝灯，状态文字使用金色。
- `Error`：红灯，并在详情中显示诊断文本。
- `Unknown`：三灯全暗，并在详情中显示诊断文本。

Reducer 的优先级从高到低为：`Error`、`WaitingApproval`、`WaitingUser`、`RunningTests`、
`Reviewing`、`RunningCommand`、`Editing`、`Thinking`。完成事件是终结事件，可以结束等待态；
`task_started`、`approval_granted`、`input_provided` 等显式恢复事件可以开始或恢复工作。

- 工作态 60 秒没有新活动后恢复为 `Idle`。
- `Completed` 保留 30 秒后恢复为 `Idle`。
- 等待态保留 5 分钟；普通活动不会覆盖等待态。
- `Error` 保留 5 分钟，显式恢复、完成或超时后解除。

快照同时记录状态进入时间和任务开始时间，主状态条显示紧凑持续时间，详情页显示完整持续时间。

## 当前 Agent 上下文

`CurrentAgentContext` 把以下字段绑定为同一份不可变快照：session、项目名、工作目录、ActivityState、
CurrentAction、简化命令、开始时间和最后更新时间。Detector 先按 session 文件分组，每个 session 独立进入
Reducer，再选择最近相关事件所属的 session，因此不会组合“旧项目名 + 新状态”。

项目识别顺序为 session `cwd` → `git rev-parse --show-toplevel` → Git 根目录名 → 普通目录名。
空路径或不存在路径不猜测项目名。Git 命令不可用或目录不是仓库时回退到 cwd 目录名；同一 cwd 不重复启动 Git 进程。

动作分类保持为纯逻辑：测试命令映射到 `Testing`，构建命令映射到 `Building`，Git 子命令映射到
`Git`，搜索命令和搜索 tool call 映射到 `Searching`，其余命令映射到 `Running`。详情页只显示
可识别的规范化命令，未知命令只保留 executable 和省略号，不保存或展示参数正文。

## 通知策略

通知只由稳定状态跃迁触发，并在首次采样时静默初始化：

- 工作态 → `WaitingApproval`
- 工作态 → `WaitingUser`
- 工作态 → `Completed`
- 任意非错误态 → `Error`

同一状态的重复轮询不会再次通知。完成与等待通知受最短任务时长阈值约束；错误通知不受该阈值影响。

## 任务栏策略

稳定默认模式是任务栏附近悬浮窗口。`AppBarInterop` 仅为后续实验性 docking 保留，因为 Windows Shell AppBar 是屏幕边缘应用栏，不等于嵌入任务栏。
