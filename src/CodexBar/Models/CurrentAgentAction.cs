namespace CodexBar.Models;

public enum CurrentAgentAction
{
    Idle,
    Thinking,
    Editing,
    Testing,
    Building,
    Running,
    Git,
    Searching,
    Reviewing,
    WaitingApproval,
    WaitingUser,
    Done,
    Error,
    Unknown
}

public static class CurrentAgentActionExtensions
{
    public static string DisplayName(this CurrentAgentAction action) => action switch
    {
        CurrentAgentAction.Idle => "Idle",
        CurrentAgentAction.Thinking => "Thinking",
        CurrentAgentAction.Editing => "Editing",
        CurrentAgentAction.Testing => "Testing",
        CurrentAgentAction.Building => "Building",
        CurrentAgentAction.Running => "Running",
        CurrentAgentAction.Git => "Git",
        CurrentAgentAction.Searching => "Searching",
        CurrentAgentAction.Reviewing => "Reviewing",
        CurrentAgentAction.WaitingApproval => "Waiting Approval",
        CurrentAgentAction.WaitingUser => "Waiting User",
        CurrentAgentAction.Done => "Done",
        CurrentAgentAction.Error => "Error",
        _ => "Unknown"
    };
}
