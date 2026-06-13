using System.Text.Json;
using CodexBar.Models;
using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexActivityDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TaskCompleteWithinGraceMapsToCompleted()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("task_complete", Now.AddSeconds(-10))
        ]);

        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }

    [Fact]
    public void TaskCompleteOlderThanGraceMapsToIdle()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("task_complete", Now.AddSeconds(-31))
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
    }

    [Fact]
    public void ActiveEventWithinWindowMapsToWorking()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("function_call", Now.AddSeconds(-20))
        ]);

        Assert.Equal(CodexActivityStatus.Working, snapshot.Status);
    }

    [Fact]
    public void ActiveEventOlderThanWindowMapsToIdle()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("reasoning", Now.AddSeconds(-61))
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
        Assert.Contains("最近未检测到新的 Codex 活动", snapshot.Detail);
    }

    [Fact]
    public void MissingTimestampDoesNotForceWorkingForever()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            @"{""type"":""event_msg"",""payload"":{""type"":""reasoning""}}"
        ]);

        Assert.Equal(CodexActivityStatus.Unknown, snapshot.Status);
    }

    [Fact]
    public void ResponseItemAfterCompletionDoesNotOverrideTaskComplete()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("agent_message", Now.AddSeconds(-5), rootType: "response_item"),
            Event("task_complete", Now.AddSeconds(-10))
        ]);

        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }

    [Fact]
    public void AnonymizedJsonlCompletionFixtureMapsToCompleted()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines(TestFixtures.ReadJsonlNewestFirst("codex-session-completed.jsonl"));

        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
        Assert.Contains("任务已完成", snapshot.Detail);
    }

    [Fact]
    public void WaitingForUserOlderThanWindowMapsToIdle()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("request_user_input", Now.AddMinutes(-6))
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
    }

    [Fact]
    public void MissingTimestampUsesFileLastWriteTime()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromEntries([
            new CodexSessionLogEntry(
                @"{""type"":""event_msg"",""payload"":{""type"":""function_call""}}",
                Now.AddSeconds(-15),
                "session.jsonl")
        ]);

        Assert.Equal(CodexActivityStatus.Working, snapshot.Status);
        Assert.Equal("session.jsonl", snapshot.SourceFile);
    }

    [Fact]
    public void NetworkFailureMessageMapsToError()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "event_msg",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                payload = new
                {
                    type = "error",
                    message = "fetch failed: ECONNRESET"
                }
            })
        ]);

        Assert.Equal(CodexActivityStatus.Error, snapshot.Status);
        Assert.Contains("网络异常", snapshot.Detail);
    }

    [Fact]
    public void AnonymizedJsonlNetworkFixtureMapsToActionableError()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines(TestFixtures.ReadJsonlNewestFirst("codex-session-network-error.jsonl"));

        Assert.Equal(CodexActivityStatus.Error, snapshot.Status);
        Assert.Contains("网络异常", snapshot.Detail);
        Assert.Contains("ECONNRESET", snapshot.Detail);
    }

    [Fact]
    public void AuthenticationFailureMessageMapsToActionableError()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "event_msg",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                payload = new
                {
                    type = "error",
                    message = "authentication failed: login required"
                }
            })
        ]);

        Assert.Equal(CodexActivityStatus.Error, snapshot.Status);
        Assert.Contains("认证已过期", snapshot.Detail);
    }

    private static CodexActivityDetector CreateDetector()
    {
        var parser = new JsonQuotaParser();
        var reader = new CodexSessionLogReader(parser, NullLogger<CodexSessionLogReader>.Instance);
        return new CodexActivityDetector(reader, NullLogger<CodexActivityDetector>.Instance, () => Now);
    }

    private static string Event(string payloadType, DateTimeOffset timestamp, string rootType = "event_msg")
        => JsonSerializer.Serialize(new
        {
            type = rootType,
            timestamp = timestamp.ToString("O"),
            payload = new { type = payloadType }
        });
}
