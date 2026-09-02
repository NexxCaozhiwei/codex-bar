using CodexBar.Models;
using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexActivityReducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PriorityKeepsApprovalAboveOrdinaryWorkAtSameTimestamp()
    {
        var reducer = new CodexActivityReducer();
        var snapshot = reducer.Reduce([
            new CodexActivityEvent(CodexActivityStatus.RunningCommand, Now),
            new CodexActivityEvent(CodexActivityStatus.WaitingApproval, Now)
        ], Now);

        Assert.NotNull(snapshot);
        Assert.Equal(CodexActivityStatus.WaitingApproval, snapshot.Status);
        Assert.True(CodexActivityReducer.Priority(CodexActivityStatus.WaitingApproval) >
                    CodexActivityReducer.Priority(CodexActivityStatus.RunningTests));
    }

    [Fact]
    public void WaitingTimeoutTransitionsToIdleAtExpiryTime()
    {
        var reducer = new CodexActivityReducer();
        var enteredAt = Now.AddMinutes(-6);
        var snapshot = reducer.Reduce([
            new CodexActivityEvent(CodexActivityStatus.WaitingUser, enteredAt)
        ], Now);

        Assert.NotNull(snapshot);
        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
        Assert.Equal(enteredAt + CodexActivityReducer.WaitingWindow, snapshot.EffectiveStateEnteredAt);
    }

    [Fact]
    public void CompletionOverridesStickyWaitingState()
    {
        var reducer = new CodexActivityReducer();
        var snapshot = reducer.Reduce([
            new CodexActivityEvent(CodexActivityStatus.WaitingApproval, Now.AddSeconds(-20)),
            new CodexActivityEvent(CodexActivityStatus.Completed, Now.AddSeconds(-5))
        ], Now);

        Assert.NotNull(snapshot);
        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }

    [Fact]
    public void CompletionWinsOverOrdinaryActivityAtSameTimestamp()
    {
        var reducer = new CodexActivityReducer();
        var snapshot = reducer.Reduce([
            new CodexActivityEvent(CodexActivityStatus.RunningCommand, Now),
            new CodexActivityEvent(CodexActivityStatus.Completed, Now)
        ], Now);

        Assert.NotNull(snapshot);
        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }
}
