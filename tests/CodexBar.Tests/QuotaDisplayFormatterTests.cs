using CodexBar.Models;
using CodexBar.ViewModels;
using Xunit;

namespace CodexBar.Tests;

public sealed class QuotaDisplayFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RemainingModeShowsRemainingPercent()
    {
        var window = Window(remainingPercent: 76, resetsAt: Now.AddHours(2.4));

        var text = QuotaDisplayFormatter.FormatSummary(window, QuotaDisplayMode.Remaining, Now);

        Assert.Equal("剩余 76%", text);
    }

    [Fact]
    public void ResetCountdownModeShowsHours()
    {
        var window = Window(remainingPercent: 76, resetsAt: Now.AddHours(2.4));

        var text = QuotaDisplayFormatter.FormatSummary(window, QuotaDisplayMode.ResetCountdown, Now);

        Assert.Equal("2.4h以后", text);
    }

    [Fact]
    public void ResetCountdownModeShowsDays()
    {
        var window = Window(remainingPercent: 55, resetsAt: Now.AddDays(3.2));

        var text = QuotaDisplayFormatter.FormatSummary(window, QuotaDisplayMode.ResetCountdown, Now);

        Assert.Equal("3.2d以后", text);
    }

    [Fact]
    public void ResetCountdownModeHandlesMissingResetTime()
    {
        var text = QuotaDisplayFormatter.FormatSummary(null, QuotaDisplayMode.ResetCountdown, Now);

        Assert.Equal("重置 --", text);
    }

    private static QuotaWindow Window(double remainingPercent, DateTimeOffset resetsAt)
        => new("5h", 300, 100 - remainingPercent, remainingPercent, resetsAt, "plus", "codex");
}
