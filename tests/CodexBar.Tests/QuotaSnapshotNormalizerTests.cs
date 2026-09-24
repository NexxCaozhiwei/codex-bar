using CodexBar.Models;
using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class QuotaSnapshotNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiredWindowPreservesLastReadingAndIsMarkedStale()
    {
        var snapshot = new QuotaSnapshot(
            Window(usedPercent: 99.6, resetsAt: Now.AddMinutes(-5)),
            Window(usedPercent: 70, resetsAt: Now.AddDays(2)),
            QuotaDataSource.JsonlFallback,
            Now.AddMinutes(-10));

        var normalized = QuotaSnapshotNormalizer.NormalizeExpiredWindows(snapshot, Now);

        Assert.Equal(99.6, normalized.FiveHour!.UsedPercent, 2);
        Assert.Equal(0.4, normalized.FiveHour.RemainingPercent, 2);
        Assert.Equal(Now.AddMinutes(-5), normalized.FiveHour.ResetsAt);
        Assert.True(normalized.FiveHour.IsStale);
        Assert.Equal(30, normalized.Weekly?.RemainingPercent);
        Assert.NotNull(normalized.Weekly?.ResetsAt);
        Assert.False(normalized.Weekly?.IsStale);
    }

    [Fact]
    public void FutureWindowIsPreserved()
    {
        var snapshot = new QuotaSnapshot(
            Window(usedPercent: 7, resetsAt: Now.AddHours(4)),
            null,
            QuotaDataSource.AppServer,
            Now);

        var normalized = QuotaSnapshotNormalizer.NormalizeExpiredWindows(snapshot, Now);

        Assert.Equal(7, normalized.FiveHour?.UsedPercent);
        Assert.Equal(93, normalized.FiveHour?.RemainingPercent);
        Assert.Equal(Now.AddHours(4), normalized.FiveHour?.ResetsAt);
        Assert.False(normalized.FiveHour?.IsStale);
    }

    [Fact]
    public void DynamicWeeklyWindowIsNormalizedWithoutCreatingFiveHourWindow()
    {
        var weekly = new QuotaWindow("7d", 10080, 45, 55, Now.AddMinutes(-1), "plus", "codex");
        var snapshot = new QuotaSnapshot(null, weekly, QuotaDataSource.AppServer, Now)
        {
            Windows = [weekly]
        };

        var normalized = QuotaSnapshotNormalizer.NormalizeExpiredWindows(snapshot, Now);

        Assert.Null(normalized.FiveHour);
        Assert.Equal(55, normalized.Weekly?.RemainingPercent);
        Assert.True(normalized.Weekly?.IsStale);
        Assert.Single(normalized.Windows);
        Assert.Equal(55, normalized.Windows[0].RemainingPercent);
        Assert.True(normalized.Windows[0].IsStale);
    }

    [Fact]
    public void FallbackWindowsRetainAccountDetailsFromAppServer()
    {
        var fallbackWindow = Window(usedPercent: 40, resetsAt: Now.AddHours(1));
        var fallback = new QuotaSnapshot(null, null, QuotaDataSource.JsonlFallback, Now)
        {
            Windows = [fallbackWindow]
        };
        var appServer = new QuotaSnapshot(null, null, QuotaDataSource.AppServer, Now)
        {
            Credits = new QuotaCredits(HasCredits: true, Unlimited: false, Balance: "42"),
            AvailableResetCredits = 2,
            PlanType = "plus",
            LimitId = "codex",
            RateLimitReachedType = "weekly"
        };

        Assert.True(appServer.HasQuotaData);
        Assert.False(appServer.HasQuotaWindows);
        Assert.True(fallback.HasQuotaWindows);

        var combined = QuotaSnapshotNormalizer.MergeFallbackWithAccountData(fallback, appServer);

        Assert.Equal(QuotaDataSource.JsonlFallback, combined.Source);
        Assert.Equal([fallbackWindow], combined.Windows);
        Assert.Equal(appServer.Credits, combined.Credits);
        Assert.Equal(2, combined.AvailableResetCredits);
        Assert.Equal("plus", combined.PlanType);
        Assert.Equal("codex", combined.LimitId);
        Assert.Equal("weekly", combined.RateLimitReachedType);
    }

    private static QuotaWindow Window(double usedPercent, DateTimeOffset resetsAt)
        => new("5h", 300, usedPercent, 100 - usedPercent, resetsAt, "plus", "codex");
}
