# Codex Bar 设计

Codex Bar 使用 Windows 原生 WPF 外壳，并用接近 MVVM 的方式组织视图模型和服务。默认界面是一个贴近任务栏通知区域的无边框悬浮窗口。它不会注入 Explorer，也不会修改 Windows 任务栏。

## 组件

- `MainWindow`：三行紧凑状态条。
- `DetailsWindow`：额度和诊断详情。
- `SettingsWindow`：用户设置编辑。
- `MainViewModel`：刷新调度和界面显示属性。
- `QuotaService`：优先 app-server，失败后回退 jsonl。
- `CodexActivityDetector`：最近 session 事件分类。
- `TrayService`：Windows Forms `NotifyIcon` 托盘集成。
- `StartupService`：当前用户开机启动注册。
- `WindowDockingService`：任务栏附近定位。

## 状态映射

- 灯位顺序固定为绿灯、蓝灯、红灯。
- `Idle` 和 `Completed`：绿灯。
- `Working`、`AutoReviewing` 和 `WaitingForUser`：蓝灯。
- `Error`：红灯，并在详情中显示诊断文本。
- `Unknown`：三灯全暗，并在详情中显示诊断文本。

## 任务栏策略

稳定默认模式是任务栏附近悬浮窗口。`AppBarInterop` 仅为后续实验性 docking 保留，因为 Windows Shell AppBar 是屏幕边缘应用栏，不等于嵌入任务栏。
