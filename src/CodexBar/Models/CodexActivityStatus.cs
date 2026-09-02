namespace CodexBar.Models;

public enum CodexActivityStatus
{
    Idle,
    Thinking,
    Editing,
    RunningCommand,
    RunningTests,
    Reviewing,
    WaitingApproval,
    WaitingUser,
    Completed,
    Unknown,
    Error
}

public static class CodexActivityStatusExtensions
{
    public static bool IsWorking(this CodexActivityStatus status)
        => status is CodexActivityStatus.Thinking
            or CodexActivityStatus.Editing
            or CodexActivityStatus.RunningCommand
            or CodexActivityStatus.RunningTests
            or CodexActivityStatus.Reviewing;

    public static bool IsWaiting(this CodexActivityStatus status)
        => status is CodexActivityStatus.WaitingApproval or CodexActivityStatus.WaitingUser;
}
