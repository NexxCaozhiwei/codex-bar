using System.Text.Json;
using CodexBar.Models;

namespace CodexBar.Services;

public sealed class JsonQuotaParser
{
    public QuotaSnapshot ParseAppServerResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseAppServerResponse(document.RootElement);
    }

    public QuotaSnapshot ParseAppServerResponse(JsonElement root)
    {
        var payload = root.TryGetProperty("result", out var result) ? result : root;
        if (TryGetByPath(payload, ["rateLimitsByLimitId", "codex"], out var codexLimits))
        {
            return ParseLimitSnapshot(
                codexLimits,
                QuotaDataSource.AppServer,
                ReadAvailableResetCredits(payload));
        }

        if (TryGetByPath(payload, ["rateLimits"], out var rateLimits))
        {
            return ParseLimitSnapshot(
                rateLimits,
                QuotaDataSource.AppServer,
                ReadAvailableResetCredits(payload));
        }

        return QuotaSnapshot.Empty("app-server 响应中没有找到 rateLimits 数据。");
    }

    public QuotaSnapshot? ParseJsonlTokenCountLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        if (!TextEquals(root, "type", "event_msg") ||
            !TryGetByPath(root, ["payload"], out var payload) ||
            !TextEquals(payload, "type", "token_count") ||
            !TryGetByPath(payload, ["rate_limits"], out var rateLimits))
        {
            return null;
        }

        if (!TextEquals(rateLimits, "limit_id", "codex"))
        {
            return null;
        }

        var timestamp = ReadDateTime(root, "timestamp") ?? DateTimeOffset.Now;
        var snapshot = ParseLimitSnapshot(rateLimits, QuotaDataSource.JsonlFallback, null, timestamp);
        return snapshot.HasQuotaData ? snapshot : null;
    }

    public static string LabelForWindow(int minutes) => minutes switch
    {
        300 => "5h",
        10080 => "7d",
        1440 => "1d",
        60 => "1h",
        _ when minutes > 0 && minutes % 1440 == 0 => $"{minutes / 1440}d",
        _ when minutes > 0 && minutes % 60 == 0 => $"{minutes / 60}h",
        _ => $"{minutes}m"
    };

    private static QuotaSnapshot BuildSnapshot(
        IReadOnlyCollection<QuotaWindow> windows,
        QuotaDataSource source,
        string? error,
        DateTimeOffset? lastRefresh = null,
        QuotaCredits? credits = null,
        int? availableResetCredits = null,
        string? planType = null,
        string? limitId = null,
        string? rateLimitReachedType = null)
    {
        var distinctWindows = windows
            .GroupBy(window => (window.WindowDurationMins, window.LimitId))
            .Select(group => group.OrderByDescending(window => window.ResetsAt).First())
            .OrderBy(window => window.WindowDurationMins)
            .ToArray();

        QuotaWindow? fiveHour = distinctWindows
            .Where(window => window.WindowDurationMins == 300)
            .OrderByDescending(window => window.ResetsAt)
            .FirstOrDefault();

        QuotaWindow? weekly = distinctWindows
            .Where(window => window.WindowDurationMins == 10080)
            .OrderByDescending(window => window.ResetsAt)
            .FirstOrDefault();

        return new QuotaSnapshot(fiveHour, weekly, source, lastRefresh ?? DateTimeOffset.Now, error)
        {
            Windows = distinctWindows,
            Credits = credits,
            AvailableResetCredits = availableResetCredits,
            PlanType = planType,
            LimitId = limitId,
            RateLimitReachedType = rateLimitReachedType
        };
    }

    private static QuotaSnapshot ParseLimitSnapshot(
        JsonElement rateLimits,
        QuotaDataSource source,
        int? availableResetCredits,
        DateTimeOffset? lastRefresh = null)
    {
        var planType = ReadString(rateLimits, "planType") ?? ReadString(rateLimits, "plan_type");
        var limitId = ReadString(rateLimits, "limitId") ?? ReadString(rateLimits, "limit_id");
        var rateLimitReachedType = ReadString(rateLimits, "rateLimitReachedType")
            ?? ReadString(rateLimits, "rate_limit_reached_type");
        var windows = new List<QuotaWindow>();
        TryAddNamedWindow(rateLimits, "primary", windows, planType, limitId);
        TryAddNamedWindow(rateLimits, "secondary", windows, planType, limitId);

        return BuildSnapshot(
            windows,
            source,
            windows.Count == 0 ? "额度响应中没有可显示的时间窗口。" : null,
            lastRefresh,
            ReadCredits(rateLimits),
            availableResetCredits,
            planType,
            limitId,
            rateLimitReachedType);
    }

    private static void TryAddNamedWindow(
        JsonElement parent,
        string propertyName,
        ICollection<QuotaWindow> windows,
        string? inheritedPlanType,
        string? inheritedLimitId)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out var window) &&
            TryParseWindow(window, inheritedPlanType, inheritedLimitId, out var quotaWindow))
        {
            windows.Add(quotaWindow);
        }
    }

    private static bool TryParseWindow(
        JsonElement element,
        string? inheritedPlanType,
        string? inheritedLimitId,
        out QuotaWindow window)
    {
        window = default!;
        var minutes = ReadInt(element, "windowDurationMins")
            ?? ReadInt(element, "windowDurationMinutes")
            ?? ReadInt(element, "window_minutes")
            ?? ReadInt(element, "windowMins")
            ?? ReadInt(element, "window_mins")
            ?? 0;
        var usedPercent = ReadDouble(element, "usedPercent") ?? ReadDouble(element, "used_percent");
        var remainingPercent = ReadDouble(element, "remainingPercent")
            ?? ReadDouble(element, "remaining_percent")
            ?? ReadDouble(element, "percentRemaining")
            ?? ReadDouble(element, "percent_remaining");

        if (minutes <= 0 || (usedPercent is null && remainingPercent is null))
        {
            return false;
        }

        var remaining = Math.Clamp(remainingPercent ?? 100d - usedPercent!.Value, 0d, 100d);
        var used = Math.Clamp(usedPercent ?? 100d - remaining, 0d, 100d);
        var resetsAt = ReadDateTime(element, "resetsAt") ?? ReadDateTime(element, "resets_at");
        var planType = ReadString(element, "planType") ?? ReadString(element, "plan_type") ?? inheritedPlanType;
        var limitId = ReadString(element, "limitId") ?? ReadString(element, "limit_id") ?? inheritedLimitId;

        window = new QuotaWindow(
            LabelForWindow(minutes),
            minutes,
            used,
            remaining,
            resetsAt,
            planType,
            limitId);
        return true;
    }

    private static QuotaCredits? ReadCredits(JsonElement rateLimits)
    {
        if (!TryGetByPath(rateLimits, ["credits"], out var credits) || credits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var hasCredits = ReadBool(credits, "hasCredits") ?? ReadBool(credits, "has_credits") ?? false;
        var unlimited = ReadBool(credits, "unlimited") ?? false;
        var balance = ReadString(credits, "balance");
        return new QuotaCredits(hasCredits, unlimited, balance);
    }

    private static int? ReadAvailableResetCredits(JsonElement payload)
        => TryGetByPath(payload, ["rateLimitResetCredits", "availableCount"], out var count)
            ? ReadIntValue(count)
            : null;

    private static bool TryGetByPath(JsonElement root, IReadOnlyList<string> path, out JsonElement element)
    {
        element = root;
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TextEquals(JsonElement element, string propertyName, string expected)
        => ReadString(element, propertyName)?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out number) ? number : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return double.TryParse(value.ToString(), out number) ? number : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static int? ReadIntValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out number) ? number : null;
    }

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
        return DateTimeOffset.TryParse(text, out var dateTime) ? dateTime : null;
    }
}
