namespace CodexBar.Models;

public enum QuotaDataSource
{
    None,
    AppServer,
    JsonlFallback
}

public sealed record QuotaWindow(
    string Label,
    int WindowDurationMins,
    double UsedPercent,
    double RemainingPercent,
    DateTimeOffset? ResetsAt,
    string? PlanType,
    string? LimitId);

public sealed record QuotaSnapshot(
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    QuotaDataSource Source,
    DateTimeOffset LastRefresh,
    string? Error = null)
{
    public static QuotaSnapshot Empty(string? error = null)
        => new(null, null, QuotaDataSource.None, DateTimeOffset.Now, error);
}

public sealed record CodexLocationResult(string? Path, string? Error)
{
    public bool Found => !string.IsNullOrWhiteSpace(Path);
}
