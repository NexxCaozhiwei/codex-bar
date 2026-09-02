using CodexBar.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace CodexBar.ViewModels;

public static class ActivityStatusPalette
{
    public static Brush StatusBrush(CodexActivityStatus status) => status switch
    {
        CodexActivityStatus.Idle or CodexActivityStatus.Completed => Brushes.LimeGreen,
        CodexActivityStatus.Thinking or
        CodexActivityStatus.Editing or
        CodexActivityStatus.RunningCommand or
        CodexActivityStatus.RunningTests or
        CodexActivityStatus.Reviewing => Brushes.DeepSkyBlue,
        CodexActivityStatus.WaitingApproval or CodexActivityStatus.WaitingUser => Brushes.Gold,
        CodexActivityStatus.Error => Brushes.OrangeRed,
        CodexActivityStatus.Unknown => Brushes.DarkGray,
        _ => Brushes.DarkGray
    };

    public static Brush GreenLightBrush(CodexActivityStatus status)
        => IsGreen(status) ? Brushes.LimeGreen : Brushes.DimGray;

    public static Brush BlueLightBrush(CodexActivityStatus status)
        => IsBlue(status) ? Brushes.DeepSkyBlue : Brushes.DimGray;

    public static Brush RedLightBrush(CodexActivityStatus status)
        => IsRed(status) ? Brushes.OrangeRed : Brushes.DimGray;

    public static bool IsGreen(CodexActivityStatus status)
        => status is CodexActivityStatus.Idle or CodexActivityStatus.Completed;

    public static bool IsBlue(CodexActivityStatus status)
        => status.IsWorking() || status.IsWaiting();

    public static bool IsRed(CodexActivityStatus status)
        => status == CodexActivityStatus.Error;
}
