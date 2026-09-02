using CodexBar.Models;
using CodexBar.Services;
using Xunit;

namespace CodexBar.Tests;

public sealed class ActionClassifierTests
{
    private readonly ActionClassifier _classifier = new();

    [Theory]
    [InlineData("dotnet test CodexBar.sln", "dotnet test")]
    [InlineData("pytest -q", "pytest")]
    public void TestCommandsMapToTesting(string command, string expectedSummary)
    {
        var result = _classifier.Classify(CodexActivityStatus.RunningCommand, command);

        Assert.Equal(CurrentAgentAction.Testing, result.Action);
        Assert.Equal(expectedSummary, result.CommandSummary);
    }

    [Fact]
    public void BuildCommandMapsToBuilding()
    {
        var result = _classifier.Classify(CodexActivityStatus.RunningCommand, "dotnet build -c Release");

        Assert.Equal(CurrentAgentAction.Building, result.Action);
        Assert.Equal("dotnet build", result.CommandSummary);
    }

    [Fact]
    public void GitCommandMapsToGit()
    {
        var result = _classifier.Classify(CodexActivityStatus.RunningCommand, "git status --short");

        Assert.Equal(CurrentAgentAction.Git, result.Action);
        Assert.Equal("git status", result.CommandSummary);
    }

    [Fact]
    public void SearchCommandMapsToSearching()
    {
        var result = _classifier.Classify(CodexActivityStatus.RunningCommand, "rg -n secret src");

        Assert.Equal(CurrentAgentAction.Searching, result.Action);
        Assert.Equal("rg", result.CommandSummary);
    }

    [Fact]
    public void ApplyPatchStateMapsToEditing()
    {
        var result = _classifier.Classify(CodexActivityStatus.Editing, "apply_patch with private content");

        Assert.Equal(CurrentAgentAction.Editing, result.Action);
        Assert.Null(result.CommandSummary);
    }

    [Fact]
    public void UnknownCommandMapsToRunningAndHidesArguments()
    {
        var result = _classifier.Classify(
            CodexActivityStatus.RunningCommand,
            "powershell.exe -Command Invoke-WebRequest -Token very-secret-value");

        Assert.Equal(CurrentAgentAction.Running, result.Action);
        Assert.Equal("powershell.exe …", result.CommandSummary);
        Assert.DoesNotContain("secret", result.CommandSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuotedExecutablePathOnlyShowsFileName()
    {
        var result = _classifier.Classify(
            CodexActivityStatus.RunningCommand,
            @"& 'C:\Program Files\Example\runner.exe' --token private-value");

        Assert.Equal(CurrentAgentAction.Running, result.Action);
        Assert.Equal("runner.exe …", result.CommandSummary);
    }
}
