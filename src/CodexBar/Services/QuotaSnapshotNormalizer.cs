using CodexBar.Models;

namespace CodexBar.Services;

public static class QuotaSnapshotNormalizer
{
    public static QuotaSnapshot NormalizeExpiredWindows(QuotaSnapshot snapshot, DateTimeOffset now)
        => snapshot with
        {
            FiveHour = NormalizeExpiredWindow(snapshot.FiveHour, now),
            Weekly = NormalizeExpiredWindow(snapshot.Weekly, now)
        };

    private static QuotaWindow? NormalizeExpiredWindow(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is null || window.ResetsAt.Value > now)
        {
            return window;
        }

        return window with
        {
            UsedPercent = 0,
            RemainingPercent = 100,
            ResetsAt = null
        };
    }
}
