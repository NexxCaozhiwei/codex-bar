# Codex Bar

Codex Bar 是一个非官方 Windows 11 桌面小工具，用于在任务栏通知区域附近显示 Codex 的工作状态和额度剩余情况。它不会注入、修改或破坏 Windows 任务栏，只以贴近任务栏的悬浮小窗运行。

```text
Codex  红 绿 蓝  正在工作
5h     [======----]     剩余 68%
7d     [========= ]     剩余 91%
```

## 功能

- 在 Windows 通知区域附近显示小型 WPF 悬浮状态条。
- 提供系统托盘图标，支持显示/隐藏、刷新、设置、开机启动、置顶、锁定位置和退出。
- 显示 5 小时额度和 7 天额度剩余。
- 从本地 Codex session jsonl 事件识别 Codex 活动状态。
- 优先读取 `codex app-server --listen stdio://`。
- app-server 不可用时回退读取 `%USERPROFILE%\.codex\sessions\**\*.jsonl`。
- 通过当前用户注册表项 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 实现开机启动。
- 设置保存到 `%APPDATA%\CodexBar\settings.json`。
- 不需要管理员权限。

## 安装

在 Windows 11 x64 上安装 .NET 8 SDK，然后构建项目或下载 Release 产物。

```powershell
dotnet restore
dotnet build -c Release
dotnet publish src/CodexBar/CodexBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o artifacts/publish/win-x64
```

运行：

```powershell
artifacts\publish\win-x64\CodexBar.exe
```

## 使用

- 左键点击状态条打开详情。
- 右键点击状态条或托盘图标打开菜单。
- 未锁定位置时可以拖动状态条。
- 在设置页中可以配置 Codex 命令路径、刷新间隔、置顶、开机启动、自动吸附和语言选项。

## Codex CLI

Codex Bar 会先使用设置中的自定义路径，然后依次检查 `where codex`、`where codex.cmd`、`where codex.exe` 和常见 npm 全局安装目录。如果找不到 Codex CLI，界面会显示明确提示，并在可能时回退读取本地 session 日志。

## 开机启动

可在托盘菜单或设置页启用开机启动。Codex Bar 只写入当前用户的 `Run` 注册表项，不需要管理员权限。

## 退出

在托盘菜单或状态条右键菜单中选择“退出”。

## 已知限制

- Codex 本地 `app-server` 接口仍是实验接口，未来可能变化。
- AppBar docking 只保留互操作基础；默认模式是更稳定的任务栏附近悬浮窗口。
- 当前版本对全屏应用避让较保守。
- 如果 app-server 和本地 jsonl 日志都不可用，额度数据会显示为不可用。

## 隐私

Codex Bar 只读取本机 Codex app-server 响应和本机 Codex session jsonl 文件。它不会上传用户代码，不会上传 Codex 日志，也不会收集遥测。

## 非官方声明

本项目是非官方工具，并非 OpenAI 官方提供。Codex 本地 app-server 接口未来可能变化。

## 许可证

MIT。见 [LICENSE](LICENSE)。

## 致谢

本项目参考了 [Tongzh-SEU/Codex-Signal-Glance](https://github.com/Tongzh-SEU/Codex-Signal-Glance) 的产品思路。原项目是 MIT 许可的 macOS Codex 额度与活动状态查看工具。Codex Bar 是 Windows 原生重写，没有复制 Swift/macOS 实现代码。
