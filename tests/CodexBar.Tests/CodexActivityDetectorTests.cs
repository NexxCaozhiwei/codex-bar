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
    public void TurnCompletedWithinGraceMapsToCompleted()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("turn_completed", Now.AddSeconds(-10))
        ]);

        Assert.Equal(CodexActivityStatus.Completed, snapshot.Status);
    }

    [Fact]
    public void CompletedToolCallRemainsWorking()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "response_item",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                item = new
                {
                    type = "custom_tool_call",
                    name = "exec_command",
                    status = "completed",
                    arguments = "{\"cmd\":\"dotnet build\"}"
                }
            })
        ]);

        Assert.Equal(CodexActivityStatus.RunningCommand, snapshot.Status);
    }

    [Fact]
    public void ItemCompletedDoesNotCompleteTask()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "item_completed",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                item = new
                {
                    type = "custom_tool_call",
                    name = "exec_command",
                    status = "completed",
                    arguments = "{\"cmd\":\"git status\"}"
                }
            })
        ]);

        Assert.Equal(CodexActivityStatus.RunningCommand, snapshot.Status);
    }

    [Fact]
    public void GenericCompletedStatusDoesNotCompleteTask()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "event_msg",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                payload = new { type = "tool_result", status = "completed" }
            })
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
    }

    [Fact]
    public void GenericCompletedEventTypeDoesNotCompleteTask()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "completed",
                timestamp = Now.AddSeconds(-5).ToString("O")
            })
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
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
    public void FunctionCallWithinWindowMapsToRunningCommand()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("function_call", Now.AddSeconds(-20))
        ]);

        Assert.Equal(CodexActivityStatus.RunningCommand, snapshot.Status);
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

        Assert.Equal(CodexActivityStatus.RunningCommand, snapshot.Status);
        Assert.Equal("session.jsonl", snapshot.SourceFile);
    }

    [Fact]
    public void StructuredEventsMapToDetailedWorkingStates()
    {
        var detector = CreateDetector();

        Assert.Equal(CodexActivityStatus.Thinking, detector.DetectFromLines([Event("reasoning", Now.AddSeconds(-5))]).Status);
        Assert.Equal(CodexActivityStatus.Editing, detector.DetectFromLines([Event("apply_patch", Now.AddSeconds(-5))]).Status);
        Assert.Equal(CodexActivityStatus.Reviewing, detector.DetectFromLines([Event("auto_review", Now.AddSeconds(-5))]).Status);
        Assert.Equal(CodexActivityStatus.WaitingApproval, detector.DetectFromLines([Event("approval_request", Now.AddSeconds(-5))]).Status);
        Assert.Equal(CodexActivityStatus.WaitingUser, detector.DetectFromLines([Event("request_user_input", Now.AddSeconds(-5))]).Status);
    }

    [Fact]
    public void TestCommandMapsToRunningTests()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            JsonSerializer.Serialize(new
            {
                type = "event_msg",
                timestamp = Now.AddSeconds(-5).ToString("O"),
                payload = new { type = "exec_command_begin", command = "dotnet test CodexBar.sln -c Release" }
            })
        ]);

        Assert.Equal(CodexActivityStatus.RunningTests, snapshot.Status);
    }

    [Fact]
    public void WaitingApprovalIsNotOverriddenByOrdinaryActivity()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("reasoning", Now.AddSeconds(-5)),
            Event("approval_request", Now.AddSeconds(-20))
        ]);

        Assert.Equal(CodexActivityStatus.WaitingApproval, snapshot.Status);
        Assert.Equal(Now.AddSeconds(-20), snapshot.EffectiveStateEnteredAt);
    }

    [Fact]
    public void ExplicitTaskStartRecoversFromWaitingState()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("task_started", Now.AddSeconds(-5)),
            Event("approval_request", Now.AddSeconds(-20))
        ]);

        Assert.Equal(CodexActivityStatus.Thinking, snapshot.Status);
    }

    [Fact]
    public void ErrorExpiresAfterRecoveryWindow()
    {
        var detector = CreateDetector();
        var snapshot = detector.DetectFromLines([
            Event("error", Now.AddMinutes(-6))
        ]);

        Assert.Equal(CodexActivityStatus.Idle, snapshot.Status);
    }

    [Fact]
    public async Task NewestSessionSwitchesProjectStateAndActionTogether()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(CommandEvent("git status", Now.AddSeconds(-5)), SourceFile: "session-b.jsonl"),
            new CodexSessionLogEntry(SessionMeta("session-b", @"C:\Repos\项目乙"), SourceFile: "session-b.jsonl"),
            new CodexSessionLogEntry(CommandEvent("dotnet test", Now.AddSeconds(-10)), SourceFile: "session-a.jsonl"),
            new CodexSessionLogEntry(SessionMeta("session-a", @"C:\Repos\project-a"), SourceFile: "session-a.jsonl")
        ]);

        Assert.Equal("session-b", context.SessionId);
        Assert.Equal("项目乙", context.ProjectName);
        Assert.Equal(CodexActivityStatus.RunningCommand, context.Activity.Status);
        Assert.Equal(CurrentAgentAction.Git, context.CurrentAction);
        Assert.Equal("git status", context.CurrentCommand);
    }

    [Fact]
    public async Task ToolWorkingDirectoryOverridesSessionWorkingDirectory()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(
                CommandEvent("dotnet test", Now.AddSeconds(-5), @"C:\Repos\中文项目"),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                SessionMeta("session-id", @"C:\Generated\english-session-slug"),
                SourceFile: "session.jsonl")
        ]);

        Assert.Equal("中文项目", context.ProjectName);
        Assert.Equal(@"C:\Repos\中文项目", context.WorkingDirectory);
        Assert.Equal(CurrentAgentAction.Testing, context.CurrentAction);
    }

    [Fact]
    public async Task JsonToolArgumentsProvideWorkingDirectory()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(
                ToolCallEvent("git status", @"C:\Repos\CodexBar", Now.AddSeconds(-5)),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                SessionMeta("session-id", @"C:\Generated\english-session-slug"),
                SourceFile: "session.jsonl")
        ]);

        Assert.Equal("CodexBar", context.ProjectName);
        Assert.Equal(@"C:\Repos\CodexBar", context.WorkingDirectory);
        Assert.Equal(CurrentAgentAction.Git, context.CurrentAction);
    }

    [Fact]
    public async Task WrappedToolInputProvidesWorkingDirectory()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(
                WrappedToolCallEvent("dotnet build", @"C:\Repos\CodexBar", Now.AddSeconds(-5)),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                SessionMeta("session-id", @"C:\Generated\english-session-slug"),
                SourceFile: "session.jsonl")
        ]);

        Assert.Equal("CodexBar", context.ProjectName);
        Assert.Equal(@"C:\Repos\CodexBar", context.WorkingDirectory);
        Assert.Equal(CurrentAgentAction.Building, context.CurrentAction);
    }

    [Fact]
    public async Task WaitingStateKeepsLatestToolWorkingDirectory()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(Event("approval_request", Now.AddSeconds(-5)), SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                CommandEvent("dotnet build", Now.AddSeconds(-20), @"C:\Repos\CodexBar"),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                SessionMeta("session-id", @"C:\Generated\english-session-slug"),
                SourceFile: "session.jsonl")
        ]);

        Assert.Equal(CodexActivityStatus.WaitingApproval, context.Activity.Status);
        Assert.Equal("CodexBar", context.ProjectName);
        Assert.Equal(@"C:\Repos\CodexBar", context.WorkingDirectory);
    }

    [Fact]
    public async Task ToolWithoutWorkdirFallsBackToSessionWorkingDirectory()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(
                CommandEvent("git status", Now.AddSeconds(-5)),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                CommandEvent("dotnet build", Now.AddSeconds(-20), @"C:\Repos\CodexBar"),
                SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(
                SessionMeta("session-id", @"C:\Generated\english-session-slug"),
                SourceFile: "session.jsonl")
        ]);

        Assert.Equal("english-session-slug", context.ProjectName);
        Assert.Equal(@"C:\Generated\english-session-slug", context.WorkingDirectory);
        Assert.Equal(CurrentAgentAction.Git, context.CurrentAction);
    }

    [Fact]
    public async Task WaitingApprovalKeepsItsActionWhenNewerCommandArrives()
    {
        var detector = CreateDetector(new StubProjectResolver());
        var context = await detector.DetectContextFromEntriesAsync([
            new CodexSessionLogEntry(CommandEvent("dotnet build", Now.AddSeconds(-5)), SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(Event("approval_request", Now.AddSeconds(-20)), SourceFile: "session.jsonl"),
            new CodexSessionLogEntry(SessionMeta("session-id", @"C:\Repos\sample"), SourceFile: "session.jsonl")
        ]);

        Assert.Equal(CodexActivityStatus.WaitingApproval, context.Activity.Status);
        Assert.Equal(CurrentAgentAction.WaitingApproval, context.CurrentAction);
        Assert.Null(context.CurrentCommand);
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

    private static CodexActivityDetector CreateDetector(IProjectResolver? projectResolver = null)
    {
        var parser = new JsonQuotaParser();
        var reader = new CodexSessionLogReader(parser, NullLogger<CodexSessionLogReader>.Instance);
        return new CodexActivityDetector(
            reader,
            new CodexActivityReducer(),
            new ActionClassifier(),
            projectResolver ?? new StubProjectResolver(),
            NullLogger<CodexActivityDetector>.Instance,
            () => Now);
    }

    private static string Event(string payloadType, DateTimeOffset timestamp, string rootType = "event_msg")
        => JsonSerializer.Serialize(new
        {
            type = rootType,
            timestamp = timestamp.ToString("O"),
            payload = new { type = payloadType }
        });

    private static string CommandEvent(string command, DateTimeOffset timestamp, string? workdir = null)
        => JsonSerializer.Serialize(new
        {
            type = "event_msg",
            timestamp = timestamp.ToString("O"),
            payload = new { type = "exec_command_begin", command, workdir }
        });

    private static string ToolCallEvent(string command, string workdir, DateTimeOffset timestamp)
        => JsonSerializer.Serialize(new
        {
            type = "response_item",
            timestamp = timestamp.ToString("O"),
            item = new
            {
                type = "custom_tool_call",
                name = "exec_command",
                arguments = JsonSerializer.Serialize(new { cmd = command, workdir })
            }
        });

    private static string WrappedToolCallEvent(string command, string workdir, DateTimeOffset timestamp)
    {
        var input = $"const result = await tools.exec_command({JsonSerializer.Serialize(new { cmd = command, workdir })});";
        return JsonSerializer.Serialize(new
        {
            type = "response_item",
            timestamp = timestamp.ToString("O"),
            payload = new
            {
                type = "custom_tool_call",
                name = "exec",
                input
            }
        });
    }

    private static string SessionMeta(string id, string cwd)
        => JsonSerializer.Serialize(new
        {
            type = "session_meta",
            payload = new { id, cwd }
        });

    private sealed class StubProjectResolver : IProjectResolver
    {
        public Task<ProjectResolution> ResolveAsync(string? workingDirectory, CancellationToken cancellationToken = default)
        {
            var projectName = string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(workingDirectory));
            return Task.FromResult(new ProjectResolution(projectName, workingDirectory, null));
        }
    }
}
