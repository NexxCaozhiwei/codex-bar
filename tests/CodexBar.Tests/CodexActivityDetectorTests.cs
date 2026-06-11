using CodexBar.Models;
using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class CodexActivityDetectorTests
{
    [Fact]
    public void TaskStartedMapsToWorking()
    {
        var detector = CreateDetector();
        var timestamp = DateTimeOffset.Now.ToString("O");
        var snapshot = detector.DetectFromLines([
            $@"{{""type"":""event_msg"",""timestamp"":""{timestamp}"",""payload"":{{""type"":""task_started""}}}}"
        ]);

        Assert.Equal(CodexActivityStatus.Working, snapshot.Status);
    }

    [Fact]
    public void TaskCompleteMapsToCompleted()
    {
        var detector = CreateDetector();
        var timestamp = DateTimeOffset.Now.ToString("O");
        var snapshot = detector.DetectFromLines([
            $@"{{""type"":""event_msg"",""timestamp"":""{timestamp}"",""payload"":{{""type"":""task_complete""}}}}"
        ]);

        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }

    [Fact]
    public void ApprovalMapsToWaitingForUser()
    {
        var detector = CreateDetector();
        var timestamp = DateTimeOffset.Now.ToString("O");
        var snapshot = detector.DetectFromLines([
            $@"{{""type"":""response_item"",""timestamp"":""{timestamp}"",""payload"":{{""tool"":""approval required permission""}}}}"
        ]);

        Assert.Equal(CodexActivityStatus.WaitingForUser, snapshot.Status);
    }

    private static CodexActivityDetector CreateDetector()
    {
        var parser = new JsonQuotaParser();
        var reader = new CodexSessionLogReader(parser, NullLogger<CodexSessionLogReader>.Instance);
        return new CodexActivityDetector(reader, NullLogger<CodexActivityDetector>.Instance);
    }
}
