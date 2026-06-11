using System.Runtime.InteropServices;

namespace CodexBar.Native;

public static class AppBarInterop
{
    public const int AbmNew = 0x00000000;
    public const int AbmRemove = 0x00000001;
    public const int AbmQueryPos = 0x00000002;
    public const int AbmSetPos = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public Rect rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern IntPtr SHAppBarMessage(int dwMessage, ref AppBarData pData);
}
