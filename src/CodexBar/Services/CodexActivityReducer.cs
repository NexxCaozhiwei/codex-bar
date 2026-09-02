using CodexBar.Models;

namespace CodexBar.Services;

public sealed class CodexActivityReducer
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan OpenTaskActiveWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CompletionGrace = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WaitingWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ErrorWindow = TimeSpan.FromMinutes(5);

    public CodexActivitySnapshot? Reduce(IEnumerable<CodexActivityEvent> events, DateTimeOffset now)
        => ReduceWithContext(events, now)?.Activity;

    public CodexActivityReduction? ReduceWithContext(IEnumerable<CodexActivityEvent> events, DateTimeOffset now)
    {
        ReducedState? current = null;

        foreach (var activityEvent in events
                     .OrderBy(item => item.Timestamp)
                     .ThenBy(item => Priority(item.Status)))
        {
            if (current is null)
            {
                current = Start(activityEvent, null);
                continue;
            }

            if (!ShouldAccept(current.Value.Event, activityEvent))
            {
                continue;
            }

            current = Start(activityEvent, current);
        }

        return current is null ? null : CreateReduction(current.Value, now);
    }

    public static int Priority(CodexActivityStatus status) => status switch
    {
        CodexActivityStatus.Error => 100,
        CodexActivityStatus.Completed => 95,
        CodexActivityStatus.WaitingApproval => 90,
        CodexActivityStatus.WaitingUser => 80,
        CodexActivityStatus.RunningTests => 70,
        CodexActivityStatus.Reviewing => 60,
        CodexActivityStatus.RunningCommand => 50,
        CodexActivityStatus.Editing => 40,
        CodexActivityStatus.Thinking => 30,
        CodexActivityStatus.Idle => 10,
        _ => 0
    };

    private static ReducedState Start(CodexActivityEvent activityEvent, ReducedState? previous)
    {
        var taskStartedAt = previous?.TaskStartedAt;
        var hasOpenTask = previous?.HasOpenTask ?? false;
        if (activityEvent.StartsTask ||
            (activityEvent.Status.IsWorking() && taskStartedAt is null))
        {
            taskStartedAt = activityEvent.Timestamp;
        }

        if (activityEvent.StartsTask)
        {
            hasOpenTask = true;
        }
        else if (activityEvent.Status is CodexActivityStatus.Completed or CodexActivityStatus.Error)
        {
            hasOpenTask = false;
        }

        return new ReducedState(activityEvent, taskStartedAt, hasOpenTask);
    }

    private static bool ShouldAccept(CodexActivityEvent current, CodexActivityEvent next)
    {
        if (next.Timestamp < current.Timestamp)
        {
            return false;
        }

        if (next.Timestamp == current.Timestamp)
        {
            return Priority(next.Status) >= Priority(current.Status);
        }

        if (next.Status is CodexActivityStatus.Error or CodexActivityStatus.Completed || next.IsExplicitRecovery)
        {
            return true;
        }

        var elapsed = next.Timestamp - current.Timestamp;
        if (current.Status.IsWaiting() && elapsed <= WaitingWindow)
        {
            return next.Status.IsWaiting() && Priority(next.Status) >= Priority(current.Status);
        }

        if (current.Status == CodexActivityStatus.Error && elapsed <= ErrorWindow)
        {
            return false;
        }

        if (current.Status == CodexActivityStatus.Completed && elapsed <= CompletionGrace)
        {
            return false;
        }

        return true;
    }

    private static CodexActivityReduction CreateReduction(ReducedState state, DateTimeOffset now)
    {
        var activityEvent = state.Event;
        var age = ClampAge(now - activityEvent.Timestamp);
        var timeout = TimeoutFor(activityEvent.Status, state.HasOpenTask);

        if (timeout is { } value && age > value)
        {
            var detail = activityEvent.Status == CodexActivityStatus.Completed
                ? "最近任务已完成，当前空闲。"
                : "最近未检测到新的 Codex 活动。";
            return new CodexActivityReduction(
                new CodexActivitySnapshot(
                    CodexActivityStatus.Idle,
                    activityEvent.Timestamp,
                    detail,
                    activityEvent.SourceFile,
                    activityEvent.Timestamp + value,
                    state.TaskStartedAt),
                activityEvent with
                {
                    Status = CodexActivityStatus.Idle,
                    CurrentAction = CurrentAgentAction.Idle,
                    CurrentCommand = null
                });
        }

        return new CodexActivityReduction(
            new CodexActivitySnapshot(
                activityEvent.Status,
                activityEvent.Timestamp,
                activityEvent.Detail ?? DetailFor(activityEvent.Status),
                activityEvent.SourceFile,
                activityEvent.Timestamp,
                state.TaskStartedAt),
            activityEvent);
    }

    private static TimeSpan? TimeoutFor(CodexActivityStatus status, bool hasOpenTask)
    {
        if (status.IsWorking())
        {
            return hasOpenTask ? OpenTaskActiveWindow : ActiveWindow;
        }

        if (status.IsWaiting())
        {
            return WaitingWindow;
        }

        return status switch
        {
            CodexActivityStatus.Completed => CompletionGrace,
            CodexActivityStatus.Error => ErrorWindow,
            _ => null
        };
    }

    private static string DetailFor(CodexActivityStatus status) => status switch
    {
        CodexActivityStatus.Thinking => "Codex 正在思考。",
        CodexActivityStatus.Editing => "Codex 正在编辑文件。",
        CodexActivityStatus.RunningCommand => "Codex 正在运行命令。",
        CodexActivityStatus.RunningTests => "Codex 正在运行测试。",
        CodexActivityStatus.Reviewing => "Codex 正在审查更改。",
        CodexActivityStatus.WaitingApproval => "正在等待用户审批或授权。",
        CodexActivityStatus.WaitingUser => "正在等待用户输入。",
        CodexActivityStatus.Completed => "任务已完成。",
        CodexActivityStatus.Error => "最近的 session 事件显示任务出错或中止。",
        _ => "未检测到活跃的 Codex 任务。"
    };

    private static TimeSpan ClampAge(TimeSpan age)
        => age < TimeSpan.Zero ? TimeSpan.Zero : age;

    private readonly record struct ReducedState(
        CodexActivityEvent Event,
        DateTimeOffset? TaskStartedAt,
        bool HasOpenTask);
}

public readonly record struct CodexActivityEvent(
    CodexActivityStatus Status,
    DateTimeOffset Timestamp,
    string? SourceFile = null,
    string? Detail = null,
    bool IsExplicitRecovery = false,
    bool StartsTask = false,
    CurrentAgentAction CurrentAction = CurrentAgentAction.Unknown,
    string? CurrentCommand = null,
    string? SessionId = null,
    string? WorkingDirectory = null);

public sealed record CodexActivityReduction(
    CodexActivitySnapshot Activity,
    CodexActivityEvent EffectiveEvent);
