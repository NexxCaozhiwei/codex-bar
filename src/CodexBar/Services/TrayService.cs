using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using CodexBar.ViewModels;
using Application = System.Windows.Application;

namespace CodexBar.Services;

public sealed class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;

    public void Initialize(Window mainWindow, MainViewModel viewModel)
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "Codex Bar 状态",
            Visible = true,
            ContextMenuStrip = BuildMenu(mainWindow, viewModel)
        };

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.Hide();
                }
                else
                {
                    ShowMainWindow(mainWindow, viewModel.Settings.TopMost);
                }
            }
        };
    }

    public void UpdateText(string text)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }
    }

    private static ContextMenuStrip BuildMenu(Window mainWindow, MainViewModel viewModel)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏", null, (_, _) =>
        {
            if (mainWindow.IsVisible)
            {
                mainWindow.Hide();
            }
            else
            {
                ShowMainWindow(mainWindow, viewModel.Settings.TopMost);
            }
        });
        menu.Items.Add("刷新", null, async (_, _) => await viewModel.RefreshAsync());
        menu.Items.Add("设置", null, (_, _) => viewModel.ShowSettings());
        menu.Items.Add("锁定位置", null, (_, _) => viewModel.ToggleLockPosition());
        menu.Items.Add("窗口置顶", null, (_, _) => viewModel.ToggleTopMost());
        menu.Items.Add("开机启动", null, (_, _) => viewModel.ToggleStartup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            viewModel.SaveWindowPosition();
            Application.Current.Shutdown();
        });
        return menu;
    }

    private static void ShowMainWindow(Window window, bool keepTopmost)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = keepTopmost;
        window.Focus();
    }

    private static Icon LoadApplicationIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }
}
