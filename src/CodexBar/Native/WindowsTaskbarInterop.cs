using System.Runtime.InteropServices;

namespace CodexBar.Native;

public static class WindowsTaskbarInterop
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    public static bool IsTaskbarAvailable()
        => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
}
