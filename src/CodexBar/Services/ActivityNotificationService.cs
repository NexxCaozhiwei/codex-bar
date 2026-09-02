using CodexBar.Models;

namespace CodexBar.Services;

public sealed class ActivityNotificationService
{
    private CodexActivitySnapshot? _previous;
    private DateTimeOffset? _workingSince;

    public ActivityNotification? Observe(CodexActivitySnapshot current, AppSettings settings)
    {
        if (_previous is null)
        {
            _previous = current;
            if (current.Status.IsWorking())
            {
                _workingSince = current.TaskStartedAt ?? current.EffectiveStateEnteredAt;
            }

            return null;
        }

        var previous = _previous;
        ActivityNotification? notification = null;

        if (current.Status.IsWorking())
        {
            if (!previous.Status.IsWorking())
            {
                _workingSince = current.TaskStartedAt ?? current.EffectiveStateEnteredAt;
            }
        }
        else if (current.Status != previous.Status)
        {
            if (current.Status == CodexActivityStatus.Error && settings.NotifyOnError)
            {
                notification = new ActivityNotification(
                    ActivityNotificationKind.Error,
                    "Codex 执行出错",
                    current.Detail);
            }
            else if (previous.Status.IsWorking())
            {
                var startedAt = current.TaskStartedAt ?? _workingSince ?? previous.EffectiveStateEnteredAt;
                var duration = current.LastEventAt - startedAt;
                if (duration < TimeSpan.Zero)
                {
                    duration = TimeSpan.Zero;
                }

                if (duration >= TimeSpan.FromSeconds(Math.Clamp(settings.NotificationMinimumTaskDurationSeconds, 0, 3600)))
                {
                    notification = CreateWorkingTransitionNotification(current.Status, duration, settings);
                }
            }

            _workingSince = null;
        }

        _previous = current;
        return notification;
    }

    private static ActivityNotification? CreateWorkingTransitionNotification(
        CodexActivityStatus status,
        TimeSpan duration,
        AppSettings settings)
    {
        var durationText = FormatDuration(duration);
        return status switch
        {
            CodexActivityStatus.WaitingApproval when settings.NotifyOnWaitingApproval => new ActivityNotification(
                ActivityNotificationKind.WaitingApproval,
                "Codex 等待审批",
                $"任务运行 {durationText}，需要你的审批或授权。"),
            CodexActivityStatus.WaitingUser when settings.NotifyOnWaitingUser => new ActivityNotification(
                ActivityNotificationKind.WaitingUser,
                "Codex 等待输入",
                $"任务运行 {durationText}，正在等待你的输入。"),
            CodexActivityStatus.Completed when settings.NotifyOnTaskCompleted => new ActivityNotification(
                ActivityNotificationKind.Completed,
                "Codex 任务已完成",
                $"本次任务耗时 {durationText}。"),
            _ => null
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(0, (int)duration.TotalSeconds)} 秒";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
    }
}

public enum ActivityNotificationKind
{
    Completed,
    WaitingUser,
    WaitingApproval,
    Error
}

public sealed record ActivityNotification(
    ActivityNotificationKind Kind,
    string Title,
    string Message);
