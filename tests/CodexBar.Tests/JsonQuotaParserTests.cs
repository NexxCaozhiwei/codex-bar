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
    public void ParsesCurrentAppServerSchemaWithUnixReset()
    {
        var json = """
        {
          "id": 2,
          "result": {
            "rateLimits": {
              "limitId": "codex",
              "limitName": null,
              "primary": { "windowDurationMins": 300, "usedPercent": 10, "resetsAt": 1781186400 },
              "secondary": { "windowDurationMins": 10080, "usedPercent": 20, "resetsAt": 1781704800 },
              "credits": null,
              "individualLimit": null,
              "planType": "plus",
              "rateLimitReachedType": null
            },
            "rateLimitsByLimitId": {
              "codex": {
                "limitId": "codex",
                "limitName": null,
                "primary": { "windowDurationMins": 300, "usedPercent": 11, "resetsAt": 1781186400 },
                "secondary": { "windowDurationMins": 10080, "usedPercent": 21, "resetsAt": 1781704800 },
                "credits": null,
                "individualLimit": null,
                "planType": "plus",
                "rateLimitReachedType": null
              }
            }
          }
        }
        """;

        var snapshot = _parser.ParseAppServerResponse(json);

        Assert.Equal(89, snapshot.FiveHour?.RemainingPercent);
        Assert.Equal(79, snapshot.Weekly?.RemainingPercent);
        Assert.NotNull(snapshot.FiveHour?.ResetsAt);
    }

    [Fact]
    public void ParsesAnonymizedAppServerFixture()
    {
        var snapshot = _parser.ParseAppServerResponse(TestFixtures.ReadText("app-server-rate-limits.json"));

        Assert.Equal("5h", snapshot.FiveHour?.Label);
        Assert.Equal(80, snapshot.FiveHour?.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 14, 24, 0, TimeSpan.Zero), snapshot.FiveHour?.ResetsAt);
        Assert.Equal("7d", snapshot.Weekly?.Label);
        Assert.Equal(55, snapshot.Weekly?.RemainingPercent);
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
    public void ParsesRemainingPercentAliases()
    {
        var json = """
        {
          "result": {
            "rateLimits": {
              "primary": { "windowDurationMinutes": 300, "remainingPercent": 25 },
              "secondary": { "windowDurationMins": 10080, "remaining_percent": 80 }
            }
          }
        }
        """;

        var snapshot = _parser.ParseAppServerResponse(json);

        Assert.Equal(25, snapshot.FiveHour?.RemainingPercent);
        Assert.Equal(75, snapshot.FiveHour?.UsedPercent);
        Assert.Equal(80, snapshot.Weekly?.RemainingPercent);
        Assert.Equal(20, snapshot.Weekly?.UsedPercent);
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

    [Fact]
    public void ParsesCurrentJsonlTokenCountShape()
    {
        var line = """
        {"timestamp":"2026-06-14T05:58:43.589Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":49287464},"last_token_usage":{"total_tokens":167234},"model_context_window":258400},"rate_limits":{"limit_id":"codex","limit_name":null,"primary":{"used_percent":7.0,"window_minutes":300,"resets_at":1781434483},"secondary":{"used_percent":71.0,"window_minutes":10080,"resets_at":1781761289},"credits":null,"individual_limit":null,"plan_type":"plus","rate_limit_reached_type":null}}}
        """;

        var snapshot = _parser.ParseJsonlTokenCountLine(line);

        Assert.NotNull(snapshot);
        Assert.Equal(93, snapshot!.FiveHour?.RemainingPercent);
        Assert.Equal(29, snapshot.Weekly?.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 10, 54, 43, TimeSpan.Zero), snapshot.FiveHour?.ResetsAt);
    }

    [Fact]
    public void ParsesAnonymizedJsonlQuotaFixture()
    {
        var snapshot = TestFixtures.ReadJsonlNewestFirst("codex-session-completed.jsonl")
            .Select(line =>
            {
                try
                {
                    return _parser.ParseJsonlTokenCountLine(line);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(parsed => parsed is not null);

        Assert.NotNull(snapshot);
        Assert.Equal(60, snapshot!.FiveHour?.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 14, 24, 0, TimeSpan.Zero), snapshot.FiveHour?.ResetsAt);
        Assert.Equal(68, snapshot.Weekly?.RemainingPercent);
    }

    [Theory]
    [InlineData(300, "5h")]
    [InlineData(10080, "7d")]
    [InlineData(1440, "1d")]
    [InlineData(60, "1h")]
    public void LabelsKnownWindows(int minutes, string label)
        => Assert.Equal(label, JsonQuotaParser.LabelForWindow(minutes));
}
