namespace CodexBar.Models;

public sealed record CurrentAgentContext(
    string? SessionId,
    string? ProjectName,
    string? WorkingDirectory,
    CodexActivitySnapshot Activity,
    CurrentAgentAction CurrentAction,
    string? CurrentCommand,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUpdatedAt)
{
    public static CurrentAgentContext Empty(CodexActivitySnapshot activity)
        => new(
            null,
            null,
            null,
            activity,
            activity.Status == CodexActivityStatus.Unknown
                ? CurrentAgentAction.Unknown
                : CurrentAgentAction.Idle,
            null,
            activity.EffectiveStateEnteredAt,
            activity.LastEventAt);
}
