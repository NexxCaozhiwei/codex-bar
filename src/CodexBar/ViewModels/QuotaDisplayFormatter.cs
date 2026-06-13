using System.Globalization;
using CodexBar.Models;

namespace CodexBar.ViewModels;

public enum QuotaDisplayMode
{
    Remaining,
    ResetCountdown
}

public static class QuotaDisplayFormatter
{
    public static string FormatSummary(QuotaWindow? window, QuotaDisplayMode mode, DateTimeOffset now)
        => mode == QuotaDisplayMode.ResetCountdown
            ? FormatResetCountdown(window, now)
            : FormatRemaining(window);

    public static string FormatRemaining(QuotaWindow? window)
        => window is null ? "剩余 --%" : $"剩余 {window.RemainingPercent:0}%";

    public static string FormatResetCountdown(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is null)
        {
            return "重置 --";
        }

        var remaining = window.ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "可重置";
        }

        return $"{FormatDuration(remaining)}以后";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var value = duration.TotalDays >= 1
            ? duration.TotalDays
            : duration.TotalHours;
        var unit = duration.TotalDays >= 1 ? "d" : "h";

        return value.ToString("0.#", CultureInfo.InvariantCulture) + unit;
    }
}
