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
    public bool NotifyOnTaskCompleted { get; set; } = true;
    public bool NotifyOnWaitingUser { get; set; } = true;
    public bool NotifyOnWaitingApproval { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
    public int NotificationMinimumTaskDurationSeconds { get; set; } = 30;
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Notify;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
}
