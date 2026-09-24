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

    private static QuotaWindow Window(double usedPercent, DateTimeOffset resetsAt)
        => new("5h", 300, usedPercent, 100 - usedPercent, resetsAt, "plus", "codex");
}
