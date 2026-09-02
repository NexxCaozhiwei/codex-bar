namespace CodexBar.Models;

public sealed record CodexActivitySnapshot(
    CodexActivityStatus Status,
    DateTimeOffset LastEventAt,
    string Detail,
    string? SourceFile = null,
    DateTimeOffset? StateEnteredAt = null,
    DateTimeOffset? TaskStartedAt = null)
{
    public DateTimeOffset EffectiveStateEnteredAt => StateEnteredAt ?? LastEventAt;
}
