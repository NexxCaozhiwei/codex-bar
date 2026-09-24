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
    string? LimitId)
{
    public bool IsStale { get; init; }
}

public sealed record QuotaCredits(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

public sealed record QuotaSnapshot(
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    QuotaDataSource Source,
    DateTimeOffset LastRefresh,
    string? Error = null)
{
    public IReadOnlyList<QuotaWindow> Windows { get; init; } = [];

    public QuotaCredits? Credits { get; init; }

    public int? AvailableResetCredits { get; init; }

    public string? PlanType { get; init; }

    public string? LimitId { get; init; }

    public string? RateLimitReachedType { get; init; }

    public bool HasQuotaData => Windows.Count > 0 || Credits is not null || AvailableResetCredits is not null;

    public static QuotaSnapshot Empty(string? error = null)
        => new(null, null, QuotaDataSource.None, DateTimeOffset.Now, error);
}

public sealed record CodexLocationResult(string? Path, string? Error)
{
    public bool Found => !string.IsNullOrWhiteSpace(Path);
}
