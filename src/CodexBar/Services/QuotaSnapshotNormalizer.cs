using CodexBar.Models;

namespace CodexBar.Services;

public static class QuotaSnapshotNormalizer
{
    public static QuotaSnapshot MergeFallbackWithAccountData(QuotaSnapshot fallback, QuotaSnapshot appServer)
        => fallback with
        {
            Credits = fallback.Credits ?? appServer.Credits,
            AvailableResetCredits = fallback.AvailableResetCredits ?? appServer.AvailableResetCredits,
            PlanType = fallback.PlanType ?? appServer.PlanType,
            LimitId = fallback.LimitId ?? appServer.LimitId,
            RateLimitReachedType = fallback.RateLimitReachedType ?? appServer.RateLimitReachedType
        };

    public static QuotaSnapshot NormalizeExpiredWindows(QuotaSnapshot snapshot, DateTimeOffset now)
    {
        var windows = snapshot.Windows.Count > 0
            ? snapshot.Windows.Select(window => NormalizeExpiredWindow(window, now)!).ToArray()
            : Array.Empty<QuotaWindow>();

        return snapshot with
        {
            Windows = windows,
            FiveHour = windows.FirstOrDefault(window => window.WindowDurationMins == 300)
                ?? NormalizeExpiredWindow(snapshot.FiveHour, now),
            Weekly = windows.FirstOrDefault(window => window.WindowDurationMins == 10080)
                ?? NormalizeExpiredWindow(snapshot.Weekly, now)
        };
    }

    private static QuotaWindow? NormalizeExpiredWindow(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is null || window.ResetsAt.Value > now)
        {
            return window;
        }

        return window with
        {
            IsStale = true
        };
    }
}
