using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexActivityDetector
{
    private readonly CodexSessionLogReader _logReader;
    private readonly ILogger<CodexActivityDetector> _logger;

    public CodexActivityDetector(CodexSessionLogReader logReader, ILogger<CodexActivityDetector> logger)
    {
        _logReader = logReader;
        _logger = logger;
    }

    public async Task<CodexActivitySnapshot> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var lines = await _logReader.ReadRecentLinesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return DetectFromLines(lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect Codex activity.");
            return new CodexActivitySnapshot(CodexActivityStatus.Unknown, DateTimeOffset.Now, ex.Message);
        }
    }

    public CodexActivitySnapshot DetectFromLines(IEnumerable<string> newestFirstLines)
    {
        foreach (var rawLine in newestFirstLines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.ToLowerInvariant();
            var timestamp = ExtractTimestamp(rawLine) ?? DateTimeOffset.Now;

            if ((DateTimeOffset.Now - timestamp).TotalMinutes > 30)
            {
                return new CodexActivitySnapshot(CodexActivityStatus.Idle, timestamp, "No recent Codex activity.");
            }

            if (ContainsAny(line, "request_user_input", "approval", "permission", "sandbox permission", "review pending", "waiting_for_user"))
            {
                return new CodexActivitySnapshot(CodexActivityStatus.WaitingForUser, timestamp, "Waiting for user input or approval.");
            }

            if (line.Contains("auto_review", StringComparison.Ordinal))
            {
                return new CodexActivitySnapshot(CodexActivityStatus.AutoReviewing, timestamp, "Auto review is running.");
            }

            if (ContainsAny(line, "task_complete", "\"completed\"", "turn_completed"))
            {
                return new CodexActivitySnapshot(CodexActivityStatus.Completed, timestamp, "Task completed.");
            }

            if (ContainsAny(
                    line,
                    "task_started",
                    "agent_message",
                    "agent_message_delta",
                    "response_item",
                    "function_call",
                    "custom_tool_call",
                    "web_search_call",
                    "reasoning",
                    "patch_apply_begin",
                    "patch_apply_end",
                    "tool_call"))
            {
                return new CodexActivitySnapshot(CodexActivityStatus.Working, timestamp, "Codex is working.");
            }

            if (ContainsAny(line, "turn_aborted", "thread_rolled_back", "\"error\""))
            {
                return new CodexActivitySnapshot(CodexActivityStatus.Error, timestamp, "Recent session event indicates an error or abort.");
            }
        }

        return new CodexActivitySnapshot(CodexActivityStatus.Idle, DateTimeOffset.Now, "No active Codex task detected.");
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));

    private static DateTimeOffset? ExtractTimestamp(string line)
    {
        var marker = "\"timestamp\"";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var colon = line.IndexOf(':', index);
        if (colon < 0)
        {
            return null;
        }

        var firstQuote = line.IndexOf('"', colon + 1);
        var secondQuote = firstQuote < 0 ? -1 : line.IndexOf('"', firstQuote + 1);
        if (firstQuote < 0 || secondQuote < 0)
        {
            return null;
        }

        var value = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }
}
