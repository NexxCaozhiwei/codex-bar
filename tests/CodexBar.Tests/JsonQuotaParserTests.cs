using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class JsonQuotaParserTests
{
    private readonly JsonQuotaParser _parser = new();

    [Fact]
    public void ParsesRateLimitsByLimitIdCodex()
    {
        var json = """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "result": {
            "rateLimitsByLimitId": {
              "codex": {
                "primary": { "windowDurationMins": 300, "usedPercent": 32, "resetsAt": "2026-06-11T10:00:00Z", "limitId": "codex" },
                "secondary": { "windowDurationMins": 10080, "usedPercent": 9, "resetsAt": "2026-06-18T10:00:00Z", "planType": "plus" }
              }
            }
          }
        }
        """;

        var snapshot = _parser.ParseAppServerResponse(json);

        Assert.Equal("5h", snapshot.FiveHour?.Label);
        Assert.Equal(68, snapshot.FiveHour?.RemainingPercent);
        Assert.Equal("7d", snapshot.Weekly?.Label);
        Assert.Equal(91, snapshot.Weekly?.RemainingPercent);
    }

    [Fact]
    public void ParsesFallbackRateLimits()
    {
        var json = """
        {
          "result": {
            "rateLimits": {
              "primary": { "window_minutes": 300, "used_percent": 40, "resets_at": "2026-06-11T10:00:00Z" },
              "secondary": { "window_minutes": 10080, "used_percent": 12 }
            }
          }
        }
        """;

        var snapshot = _parser.ParseAppServerResponse(json);

        Assert.Equal(60, snapshot.FiveHour?.RemainingPercent);
        Assert.Equal(88, snapshot.Weekly?.RemainingPercent);
    }

    [Fact]
    public void ParsesJsonlTokenCount()
    {
        var line = """
        {"type":"event_msg","timestamp":"2026-06-11T09:00:00Z","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"windowDurationMins":300,"usedPercent":1},"secondary":{"windowDurationMins":10080,"usedPercent":2}}}}
        """;

        var snapshot = _parser.ParseJsonlTokenCountLine(line);

        Assert.NotNull(snapshot);
        Assert.Equal(99, snapshot!.FiveHour?.RemainingPercent);
        Assert.Equal(98, snapshot.Weekly?.RemainingPercent);
    }

    [Theory]
    [InlineData(300, "5h")]
    [InlineData(10080, "7d")]
    [InlineData(1440, "1d")]
    [InlineData(60, "1h")]
    public void LabelsKnownWindows(int minutes, string label)
        => Assert.Equal(label, JsonQuotaParser.LabelForWindow(minutes));
}
