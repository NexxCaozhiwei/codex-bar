using System.Windows;
using System.Windows.Forms;
using CodexBar.Models;

namespace CodexBar.Services;

public sealed class WindowDockingService
{
    public void Apply(Window window, AppSettings settings)
    {
        window.Topmost = settings.TopMost;

        if (!settings.AutoDockToTaskbar && !double.IsNaN(settings.Left) && !double.IsNaN(settings.Top))
        {
            window.Left = settings.Left;
            window.Top = settings.Top;
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

    private static Point GetScale(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null)
        {
            return new Point(1, 1);
        }

        return new Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22);
    }
}
