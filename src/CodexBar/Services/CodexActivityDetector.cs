using System.Text.Json;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexActivityDetector
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CompletionGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WaitingWindow = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> ActiveEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "task_started",
        "function_call",
        "custom_tool_call",
        "web_search_call",
        "tool_call",
        "patch_apply_begin",
        "reasoning",
        "agent_message_delta",
        "exec_command_begin",
        "command_execution_begin"
    };

    private static readonly HashSet<string> CompletionEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "task_complete",
        "turn_completed",
        "completed"
    };

    private static readonly HashSet<string> WaitingEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "request_user_input",
        "approval",
        "permission",
        "sandbox_permission",
        "waiting_for_user",
        "review_pending"
    };

    private static readonly HashSet<string> ErrorEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "turn_aborted",
        "thread_rolled_back",
        "error",
        "failed",
        "failure",
        "network_error",
        "connection_error",
        "timeout",
        "timed_out",
        "disconnected"
    };

    private readonly CodexSessionLogReader _logReader;
    private readonly ILogger<CodexActivityDetector> _logger;
    private readonly Func<DateTimeOffset> _now;

    public CodexActivityDetector(
        CodexSessionLogReader logReader,
        ILogger<CodexActivityDetector> logger,
        Func<DateTimeOffset>? now = null)
    {
        _logReader = logReader;
        _logger = logger;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<CodexActivitySnapshot> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await _logReader.ReadRecentLogEntriesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return DetectFromEntries(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检测 Codex 活动状态失败。");
            return new CodexActivitySnapshot(CodexActivityStatus.Unknown, _now(), ex.Message);
        }
    }

    public CodexActivitySnapshot DetectFromLines(IEnumerable<string> newestFirstLines)
        => DetectFromEntries(newestFirstLines.Select(line => new CodexSessionLogEntry(line)));

    public CodexActivitySnapshot DetectFromEntries(IEnumerable<CodexSessionLogEntry> newestFirstEntries)
    {
        var now = _now();
        ActivityEvent? newestRelevant = null;
        ActivityEvent? newestCompletion = null;
        var sawUnclassifiableWithoutTime = false;

        foreach (var entry in newestFirstEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Line))
            {
                continue;
            }

            if (!TryParseActivityEvent(entry, out var activityEvent))
            {
                sawUnclassifiableWithoutTime = true;
                continue;
            }

            if (activityEvent.Kind == ActivityEventKind.Ignore)
            {
                continue;
            }

            if (newestRelevant is null || activityEvent.Timestamp > newestRelevant.Value.Timestamp)
            {
                newestRelevant = activityEvent;
            }

            if (activityEvent.Kind == ActivityEventKind.Completed &&
                (newestCompletion is null || activityEvent.Timestamp > newestCompletion.Value.Timestamp))
            {
                newestCompletion = activityEvent;
            }
        }

        if (newestRelevant is null)
        {
            return sawUnclassifiableWithoutTime
                ? new CodexActivitySnapshot(CodexActivityStatus.Unknown, now, "无法判断最近 Codex 活动时间。")
                : new CodexActivitySnapshot(CodexActivityStatus.Idle, now, "未检测到活跃的 Codex 任务。");
        }

        var latest = newestRelevant.Value;
        var latestCompletion = newestCompletion;
        var age = ClampAge(now - latest.Timestamp);
        return latest.Kind switch
        {
            ActivityEventKind.Waiting when age <= WaitingWindow => new CodexActivitySnapshot(
                CodexActivityStatus.WaitingForUser,
                latest.Timestamp,
                "正在等待用户输入或授权。",
                latest.SourceFile),

            ActivityEventKind.Waiting => Idle(latest, "最近未检测到新的 Codex 活动。"),

            ActivityEventKind.Completed when age <= CompletionGrace => new CodexActivitySnapshot(
                CodexActivityStatus.Completed,
                latest.Timestamp,
                "任务已完成。",
                latest.SourceFile),

            ActivityEventKind.Completed => Idle(latest, "最近任务已完成，当前空闲。"),

            ActivityEventKind.Active when IsAfterRecentCompletion(latest, latestCompletion) => Idle(
                latestCompletion!.Value,
                "最近任务已完成，当前空闲。"),

            ActivityEventKind.Active when age <= ActiveWindow => new CodexActivitySnapshot(
                CodexActivityStatus.Working,
                latest.Timestamp,
                "Codex 正在工作。",
                latest.SourceFile),

            ActivityEventKind.Active => Idle(latest, "最近未检测到新的 Codex 活动。"),

            ActivityEventKind.Error => new CodexActivitySnapshot(
                CodexActivityStatus.Error,
                latest.Timestamp,
                "最近的 session 事件显示任务出错或中止。",
                latest.SourceFile),

            _ => new CodexActivitySnapshot(CodexActivityStatus.Unknown, latest.Timestamp, "无法判断最近 Codex 活动状态。", latest.SourceFile)
        };
    }

    private static CodexActivitySnapshot Idle(ActivityEvent activityEvent, string detail)
        => new(CodexActivityStatus.Idle, activityEvent.Timestamp, detail, activityEvent.SourceFile);

    private static bool IsAfterRecentCompletion(ActivityEvent activeEvent, ActivityEvent? completionEvent)
        => completionEvent is not null && completionEvent.Value.Timestamp >= activeEvent.Timestamp;

    private static TimeSpan ClampAge(TimeSpan age)
        => age < TimeSpan.Zero ? TimeSpan.Zero : age;

    private static bool TryParseActivityEvent(CodexSessionLogEntry entry, out ActivityEvent activityEvent)
    {
        activityEvent = default;

        try
        {
            using JsonDocument document = JsonDocument.Parse(entry.Line);
            var root = document.RootElement;
            var timestamp = ReadTimestamp(root, entry.FileLastWriteTimeUtc);
            if (timestamp is null)
            {
                return false;
            }

            var kind = Classify(root);
            activityEvent = new ActivityEvent(kind, timestamp.Value, entry.SourceFile);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ActivityEventKind Classify(JsonElement root)
    {
        var values = EnumerateSemanticValues(root).ToArray();

        if (values.Any(IsWaitingValue))
        {
            return ActivityEventKind.Waiting;
        }

        if (values.Any(IsErrorValue))
        {
            return ActivityEventKind.Error;
        }

        if (values.Any(value => CompletionEvents.Contains(value)))
        {
            return ActivityEventKind.Completed;
        }

        if (values.Any(IsActiveValue))
        {
            return ActivityEventKind.Active;
        }

        return ActivityEventKind.Ignore;
    }

    private static bool IsActiveValue(string value)
        => ActiveEvents.Contains(value) ||
           value.EndsWith("/begin", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith("_begin", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaitingValue(string value)
        => WaitingEvents.Contains(value) ||
           value.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("waiting_for_user", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorValue(string value)
        => ErrorEvents.Contains(value) ||
           value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("network", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("fetch failed", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("econnreset", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("enotfound", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("etimedout", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("tls", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateSemanticValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var value in ReadStringProperties(element, "type", "event", "event_type", "kind", "method", "name", "status", "error", "message", "detail", "reason", "code", "error_type"))
        {
            yield return value;
        }

        if (element.TryGetProperty("payload", out var payload))
        {
            foreach (var value in EnumerateSemanticValues(payload))
            {
                yield return value;
            }
        }

        if (element.TryGetProperty("item", out var item))
        {
            foreach (var value in EnumerateSemanticValues(item))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ReadStringProperties(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, DateTimeOffset? fileLastWriteTimeUtc)
    {
        return ReadDateTime(root, "timestamp")
            ?? ReadNestedDateTime(root, "payload", "completed_at")
            ?? ReadNestedDateTime(root, "payload", "completedAt")
            ?? ReadNestedDateTime(root, "payload", "created_at")
            ?? ReadNestedDateTime(root, "payload", "createdAt")
            ?? ReadNestedDateTime(root, "item", "completed_at")
            ?? ReadNestedDateTime(root, "item", "completedAt")
            ?? fileLastWriteTimeUtc;
    }

    private static DateTimeOffset? ReadNestedDateTime(JsonElement root, string parentName, string propertyName)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(parentName, out var parent)
            ? ReadDateTime(parent, propertyName)
            : null;

    private static DateTimeOffset? ReadDateTime(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return DateTimeOffset.TryParse(text, out var timestamp) ? timestamp : null;
    }

    private enum ActivityEventKind
    {
        Ignore,
        Active,
        Waiting,
        Completed,
        Error
    }

    private readonly record struct ActivityEvent(ActivityEventKind Kind, DateTimeOffset Timestamp, string? SourceFile);
}
