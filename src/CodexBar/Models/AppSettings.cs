namespace CodexBar.Models;

public sealed class AppSettings
{
    public string? CodexPath { get; set; }
    public bool StartWithWindows { get; set; }
    public bool TopMost { get; set; } = true;
    public bool LockPosition { get; set; }
    public bool AutoDockToTaskbar { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 15;
    public int OpacityPercent { get; set; } = 100;
    public string Language { get; set; } = "zh";
    public double? Left { get; set; }
    public double? Top { get; set; }
}
