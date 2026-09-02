using CodexBar.Models;
using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class ActivityNotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstObservationNeverNotifies()
    {
        var service = new ActivityNotificationService();

        var notification = service.Observe(Snapshot(CodexActivityStatus.Error, Now), new AppSettings());

        Assert.Null(notification);
    }

    [Theory]
    [InlineData(CodexActivityStatus.WaitingApproval, ActivityNotificationKind.WaitingApproval)]
    [InlineData(CodexActivityStatus.WaitingUser, ActivityNotificationKind.WaitingUser)]
    [InlineData(CodexActivityStatus.Completed, ActivityNotificationKind.Completed)]
    public void WorkingTransitionsCreateKeyNotifications(CodexActivityStatus target, ActivityNotificationKind expected)
    {
        var service = new ActivityNotificationService();
        var settings = new AppSettings { NotificationMinimumTaskDurationSeconds = 30 };
        var startedAt = Now.AddMinutes(-2);
        service.Observe(Snapshot(CodexActivityStatus.RunningTests, startedAt), settings);

        var notification = service.Observe(Snapshot(target, Now, startedAt), settings);

        Assert.NotNull(notification);
        Assert.Equal(expected, notification.Kind);
    }

    [Fact]
    public void MinimumDurationSuppressesShortTaskNotification()
    {
        var service = new ActivityNotificationService();
        var settings = new AppSettings { NotificationMinimumTaskDurationSeconds = 60 };
        var startedAt = Now.AddSeconds(-10);
        service.Observe(Snapshot(CodexActivityStatus.Thinking, startedAt), settings);

        var notification = service.Observe(Snapshot(CodexActivityStatus.Completed, Now, startedAt), settings);

        Assert.Null(notification);
    }

    [Fact]
    public void ErrorNotificationIgnoresMinimumDurationAndDoesNotRepeat()
    {
        var service = new ActivityNotificationService();
        var settings = new AppSettings { NotificationMinimumTaskDurationSeconds = 3600 };
        service.Observe(Snapshot(CodexActivityStatus.Idle, Now.AddSeconds(-1)), settings);

        var first = service.Observe(Snapshot(CodexActivityStatus.Error, Now), settings);
        var repeated = service.Observe(Snapshot(CodexActivityStatus.Error, Now.AddSeconds(1)), settings);

        Assert.NotNull(first);
        Assert.Equal(ActivityNotificationKind.Error, first.Kind);
        Assert.Null(repeated);
    }

    [Fact]
    public void DisabledCategoryDoesNotNotify()
    {
        var service = new ActivityNotificationService();
        var settings = new AppSettings
        {
            NotificationMinimumTaskDurationSeconds = 0,
            NotifyOnWaitingApproval = false
        };
        var startedAt = Now.AddMinutes(-1);
        service.Observe(Snapshot(CodexActivityStatus.Editing, startedAt), settings);

        var notification = service.Observe(Snapshot(CodexActivityStatus.WaitingApproval, Now, startedAt), settings);

        Assert.Null(notification);
    }

    private static CodexActivitySnapshot Snapshot(
        CodexActivityStatus status,
        DateTimeOffset at,
        DateTimeOffset? taskStartedAt = null)
        => new(status, at, status.ToString(), StateEnteredAt: at, TaskStartedAt: taskStartedAt ?? at);
}
