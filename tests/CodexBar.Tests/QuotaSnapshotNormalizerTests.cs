using CodexBar.Models;
using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class QuotaSnapshotNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiredWindowIsTreatedAsReset()
    {
        var snapshot = new QuotaSnapshot(
            Window(usedPercent: 99.6, resetsAt: Now.AddMinutes(-5)),
            Window(usedPercent: 70, resetsAt: Now.AddDays(2)),
            QuotaDataSource.JsonlFallback,
            Now.AddMinutes(-10));

        var normalized = QuotaSnapshotNormalizer.NormalizeExpiredWindows(snapshot, Now);

        Assert.Equal(0, normalized.FiveHour?.UsedPercent);
        Assert.Equal(100, normalized.FiveHour?.RemainingPercent);
        Assert.Null(normalized.FiveHour?.ResetsAt);
        Assert.Equal(30, normalized.Weekly?.RemainingPercent);
        Assert.NotNull(normalized.Weekly?.ResetsAt);
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
    }

    private static QuotaWindow Window(double usedPercent, DateTimeOffset resetsAt)
        => new("5h", 300, usedPercent, 100 - usedPercent, resetsAt, "plus", "codex");
}
