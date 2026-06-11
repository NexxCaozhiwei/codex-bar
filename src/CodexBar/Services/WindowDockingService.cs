using System.Windows;
using System.Windows.Forms;
using CodexBar.Models;

namespace CodexBar.Services;

public sealed class WindowDockingService
{
    public void Apply(Window window, AppSettings settings)
    {
        window.Topmost = settings.TopMost;

        if (!settings.AutoDockToTaskbar &&
            settings.Left is not null &&
            settings.Top is not null &&
            IsVisibleOnVirtualScreen(window, settings.Left.Value, settings.Top.Value))
        {
            window.Left = settings.Left.Value;
            window.Top = settings.Top.Value;
            return;
        }

        DockNearTaskbar(window);
    }

    public void DockNearTaskbar(Window window)
    {
        var screen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var scale = GetScale(window);

        var width = window.Width * scale.X;
        var height = window.Height * scale.Y;
        var margin = 10d;

        window.Left = (area.Right - width - margin) / scale.X;
        window.Top = (area.Bottom - height - margin) / scale.Y;
    }

    private static System.Windows.Point GetScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
        {
            return new System.Windows.Point(1, 1);
        }

        return new System.Windows.Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22);
    }

    private static bool IsVisibleOnVirtualScreen(Window window, double left, double top)
    {
        var windowRect = new Rect(left, top, Math.Max(1, window.Width), Math.Max(1, window.Height));
        var screenRect = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        windowRect.Intersect(screenRect);
        return windowRect.Width >= 32 && windowRect.Height >= 24;
    }
}
