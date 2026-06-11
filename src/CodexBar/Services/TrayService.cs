using System.Drawing;
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
            Icon = SystemIcons.Application,
            Text = "Codex Bar",
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
                    mainWindow.Show();
                    mainWindow.Activate();
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
        menu.Items.Add("Show / Hide", null, (_, _) =>
        {
            if (mainWindow.IsVisible) mainWindow.Hide(); else mainWindow.Show();
        });
        menu.Items.Add("Refresh", null, async (_, _) => await viewModel.RefreshAsync());
        menu.Items.Add("Settings", null, (_, _) => viewModel.ShowSettings());
        menu.Items.Add("Lock position", null, (_, _) => viewModel.ToggleLockPosition());
        menu.Items.Add("Top most", null, (_, _) => viewModel.ToggleTopMost());
        menu.Items.Add("Start with Windows", null, (_, _) => viewModel.ToggleStartup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            viewModel.SaveWindowPosition();
            Application.Current.Shutdown();
        });
        return menu;
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
