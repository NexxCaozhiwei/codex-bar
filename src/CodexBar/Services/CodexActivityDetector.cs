using System.Text.Json;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexActivityDetector
{
    private static readonly HashSet<string> ThinkingEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "task_started", "turn_started", "reasoning", "agent_message_delta"
    };

    private static readonly HashSet<string> EditingEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply_patch", "patch_apply_begin", "file_edit", "file_change", "edit_file", "write_file"
    };

    private static readonly HashSet<string> CommandEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "function_call", "custom_tool_call", "web_search_call", "tool_call", "exec_command",
        "exec_command_begin", "command_execution_begin"
    };

    private static readonly HashSet<string> ReviewEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto_review", "auto_reviewing", "code_review", "reviewing", "review_started"
    };

    private static readonly HashSet<string> CompletionEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "task_complete", "turn_completed", "completed"
    };

    private static readonly HashSet<string> WaitingUserEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "request_user_input", "waiting_for_user", "user_input_required"
    };

    private static readonly HashSet<string> WaitingApprovalEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "approval", "approval_request", "permission", "permission_request", "sandbox_permission", "review_pending"
    };

    private static readonly HashSet<string> RecoveryEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "approval_granted", "permission_granted", "input_provided", "user_message", "resumed", "task_resumed"
    };

    private static readonly HashSet<string> ErrorEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "turn_aborted", "thread_rolled_back", "error", "failed", "failure", "network_error",
        "connection_error", "timeout", "timed_out", "disconnected"
    };

    private readonly CodexSessionLogReader _logReader;
    private readonly CodexActivityReducer _reducer;
    private readonly ILogger<CodexActivityDetector> _logger;
    private readonly Func<DateTimeOffset> _now;

    public CodexActivityDetector(
        CodexSessionLogReader logReader,
        CodexActivityReducer reducer,
        ILogger<CodexActivityDetector> logger,
        Func<DateTimeOffset>? now = null)
    {
        _logReader = logReader;
        _reducer = reducer;
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
            var now = _now();
            return new CodexActivitySnapshot(
                CodexActivityStatus.Error,
                now,
                CodexDiagnostics.DescribeSessionLogFailure(ex.Message),
                StateEnteredAt: now);
        }
    }

    public CodexActivitySnapshot DetectFromLines(IEnumerable<string> newestFirstLines)
        => DetectFromEntries(newestFirstLines.Select(line => new CodexSessionLogEntry(line)));

    public CodexActivitySnapshot DetectFromEntries(IEnumerable<CodexSessionLogEntry> newestFirstEntries)
    {
        var now = _now();
        var events = new List<CodexActivityEvent>();
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

            if (activityEvent is { } value)
            {
                events.Add(value);
            }
        }

        var snapshot = _reducer.Reduce(events, now);
        if (snapshot is not null)
        {
            return snapshot;
        }

        return sawUnclassifiableWithoutTime
            ? new CodexActivitySnapshot(CodexActivityStatus.Unknown, now, "无法判断最近 Codex 活动时间。", StateEnteredAt: now)
            : new CodexActivitySnapshot(CodexActivityStatus.Idle, now, "未检测到活跃的 Codex 任务。", StateEnteredAt: now);
    }

    private static bool TryParseActivityEvent(CodexSessionLogEntry entry, out CodexActivityEvent? activityEvent)
    {
        activityEvent = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(entry.Line);
            var root = document.RootElement;
            var timestamp = ReadTimestamp(root, entry.FileLastWriteTimeUtc);
            if (timestamp is null)
            {
                return false;
            }

            var classification = Classify(root);
            if (classification is null)
            {
                return true;
            }

            activityEvent = new CodexActivityEvent(
                classification.Value.Status,
                timestamp.Value,
                entry.SourceFile,
                classification.Value.Detail,
                classification.Value.IsExplicitRecovery,
                classification.Value.StartsTask);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ActivityClassification? Classify(JsonElement root)
    {
        var values = EnumerateSemanticValues(root).ToArray();

        if (values.Any(IsErrorValue))
        {
            return new ActivityClassification(
                CodexActivityStatus.Error,
                CodexDiagnostics.DescribeActivityError(string.Join(" ", values)));
        }

        if (values.Any(value => CompletionEvents.Contains(value)))
        {
            return new ActivityClassification(CodexActivityStatus.Completed);
        }

        if (values.Any(value => RecoveryEvents.Contains(value)))
        {
            return new ActivityClassification(CodexActivityStatus.Thinking, IsExplicitRecovery: true);
        }

        if (values.Any(IsWaitingApprovalValue))
        {
            return new ActivityClassification(CodexActivityStatus.WaitingApproval);
        }

        if (values.Any(IsWaitingUserValue))
        {
            return new ActivityClassification(CodexActivityStatus.WaitingUser);
        }

        var commandValues = EnumerateCommandValues(root).ToArray();
        if (commandValues.Any(IsTestCommand))
        {
            return new ActivityClassification(CodexActivityStatus.RunningTests);
        }

        if (values.Any(IsReviewValue))
        {
            return new ActivityClassification(CodexActivityStatus.Reviewing);
        }

        if (values.Any(IsEditingValue))
        {
            return new ActivityClassification(CodexActivityStatus.Editing);
        }

        if (values.Any(IsCommandValue) || commandValues.Length > 0)
        {
            return new ActivityClassification(CodexActivityStatus.RunningCommand);
        }

        if (values.Any(value => ThinkingEvents.Contains(value)))
        {
            var startsTask = values.Any(value => value.Equals("task_started", StringComparison.OrdinalIgnoreCase));
            return new ActivityClassification(CodexActivityStatus.Thinking, IsExplicitRecovery: startsTask, StartsTask: startsTask);
        }

        return null;
    }

    private static bool IsEditingValue(string value)
        => EditingEvents.Contains(value) ||
           value.Contains("apply_patch", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("file_edit", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommandValue(string value)
        => CommandEvents.Contains(value) ||
           value.EndsWith("/begin", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith("_begin", StringComparison.OrdinalIgnoreCase);

    private static bool IsReviewValue(string value)
        => ReviewEvents.Contains(value) || value.Contains("auto_review", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaitingApprovalValue(string value)
        => WaitingApprovalEvents.Contains(value) ||
           value.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("permission", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaitingUserValue(string value)
        => WaitingUserEvents.Contains(value) ||
           value.Contains("waiting_for_user", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("request_user_input", StringComparison.OrdinalIgnoreCase);

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

    private static bool IsTestCommand(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        return normalized.Contains("dotnet test", StringComparison.Ordinal) ||
               normalized.Contains("pytest", StringComparison.Ordinal) ||
               normalized.Contains("python -m pytest", StringComparison.Ordinal) ||
               normalized.Contains("npm test", StringComparison.Ordinal) ||
               normalized.Contains("npm run test", StringComparison.Ordinal) ||
               normalized.Contains("pnpm test", StringComparison.Ordinal) ||
               normalized.Contains("yarn test", StringComparison.Ordinal) ||
               normalized.Contains("cargo test", StringComparison.Ordinal) ||
               normalized.Contains("go test", StringComparison.Ordinal) ||
               normalized.Contains("mvn test", StringComparison.Ordinal) ||
               normalized.Contains("gradle test", StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateSemanticValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var value in ReadStringProperties(
                     element,
                     "type", "event", "event_type", "eventType", "kind", "method", "name", "status",
                     "error", "message", "detail", "reason", "code", "error_type", "errorType"))
        {
            yield return value;
        }

        foreach (var childName in new[] { "payload", "item" })
        {
            if (!element.TryGetProperty(childName, out var child))
            {
                continue;
            }

            foreach (var value in EnumerateSemanticValues(child))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateCommandValues(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var value in ReadStringProperties(element, "command", "cmd", "arguments"))
        {
            yield return value;
        }

        foreach (var childName in new[] { "payload", "item" })
        {
            if (!element.TryGetProperty(childName, out var child))
            {
                continue;
            }

            foreach (var value in EnumerateCommandValues(child))
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

    private readonly record struct ActivityClassification(
        CodexActivityStatus Status,
        string? Detail = null,
        bool IsExplicitRecovery = false,
        bool StartsTask = false);
}
