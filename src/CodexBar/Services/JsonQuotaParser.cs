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
        var windows = new List<QuotaWindow>();

        if (TryGetByPath(payload, ["rateLimitsByLimitId", "codex"], out var codexLimits))
        {
            TryAddNamedWindow(codexLimits, "primary", windows);
            TryAddNamedWindow(codexLimits, "secondary", windows);
        }

        if (TryGetByPath(payload, ["rateLimits"], out var rateLimits))
        {
            TryAddNamedWindow(rateLimits, "primary", windows);
            TryAddNamedWindow(rateLimits, "secondary", windows);
        }

        if (windows.Count == 0)
        {
            return QuotaSnapshot.Empty("No rateLimits object was found in app-server response.");
        }

        return BuildSnapshot(windows, QuotaDataSource.AppServer, null);
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

        var windows = new List<QuotaWindow>();
        TryAddNamedWindow(rateLimits, "primary", windows);
        TryAddNamedWindow(rateLimits, "secondary", windows);

        if (windows.Count == 0)
        {
            return null;
        }

        var timestamp = ReadDateTime(root, "timestamp") ?? DateTimeOffset.Now;
        return BuildSnapshot(windows, QuotaDataSource.JsonlFallback, null, timestamp);
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
        DateTimeOffset? lastRefresh = null)
    {
        QuotaWindow? fiveHour = windows
            .Where(window => window.WindowDurationMins == 300)
            .OrderByDescending(window => window.ResetsAt)
            .FirstOrDefault();

        QuotaWindow? weekly = windows
            .Where(window => window.WindowDurationMins == 10080)
            .OrderByDescending(window => window.ResetsAt)
            .FirstOrDefault();

        fiveHour ??= windows.OrderBy(window => window.WindowDurationMins).FirstOrDefault();
        weekly ??= windows.OrderByDescending(window => window.WindowDurationMins).FirstOrDefault(window => window != fiveHour);

        return new QuotaSnapshot(fiveHour, weekly, source, lastRefresh ?? DateTimeOffset.Now, error);
    }

    private static void TryAddNamedWindow(JsonElement parent, string propertyName, ICollection<QuotaWindow> windows)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out var window) &&
            TryParseWindow(window, out var quotaWindow))
        {
            windows.Add(quotaWindow);
        }
    }

    private static bool TryParseWindow(JsonElement element, out QuotaWindow window)
    {
        window = default!;
        var minutes = ReadInt(element, "windowDurationMins") ?? ReadInt(element, "window_minutes") ?? 0;
        var usedPercent = ReadDouble(element, "usedPercent") ?? ReadDouble(element, "used_percent");

        if (minutes <= 0 || usedPercent is null)
        {
            return false;
        }

        var remaining = Math.Clamp(100d - usedPercent.Value, 0d, 100d);
        var resetsAt = ReadDateTime(element, "resetsAt") ?? ReadDateTime(element, "resets_at");
        var planType = ReadString(element, "planType") ?? ReadString(element, "plan_type");
        var limitId = ReadString(element, "limitId") ?? ReadString(element, "limit_id");

        window = new QuotaWindow(
            LabelForWindow(minutes),
            minutes,
            Math.Clamp(usedPercent.Value, 0d, 100d),
            remaining,
            resetsAt,
            planType,
            limitId);
        return true;
    }

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

    private static DateTimeOffset? ReadDateTime(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var dateTime) ? dateTime : null;
    }
}
