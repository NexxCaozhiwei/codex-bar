using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexBar.Models;

namespace CodexBar.Services;

public sealed partial class ActionClassifier
{
    private const int MaximumCommandSummaryLength = 48;

    public ActionClassification Classify(
        CodexActivityStatus status,
        string? rawCommand = null,
        IEnumerable<string>? semanticValues = null)
    {
        var command = ExtractCommand(rawCommand);
        var values = semanticValues?.ToArray() ?? [];

        return status switch
        {
            CodexActivityStatus.Idle => new(CurrentAgentAction.Idle),
            CodexActivityStatus.Thinking => new(CurrentAgentAction.Thinking),
            CodexActivityStatus.Editing => new(CurrentAgentAction.Editing),
            CodexActivityStatus.RunningTests => new(CurrentAgentAction.Testing, CanonicalTestCommand(command)),
            CodexActivityStatus.Reviewing => new(CurrentAgentAction.Reviewing),
            CodexActivityStatus.WaitingApproval => new(CurrentAgentAction.WaitingApproval),
            CodexActivityStatus.WaitingUser => new(CurrentAgentAction.WaitingUser),
            CodexActivityStatus.Completed => new(CurrentAgentAction.Done),
            CodexActivityStatus.Error => new(CurrentAgentAction.Error),
            CodexActivityStatus.Unknown => new(CurrentAgentAction.Unknown),
            CodexActivityStatus.RunningCommand => ClassifyCommand(command, values),
            _ => new(CurrentAgentAction.Unknown)
        };
    }

    private static ActionClassification ClassifyCommand(string? command, IReadOnlyCollection<string> values)
    {
        if (IsTestCommand(command))
        {
            return new ActionClassification(CurrentAgentAction.Testing, CanonicalTestCommand(command));
        }

        if (TryCanonicalBuildCommand(command, out var buildCommand))
        {
            return new ActionClassification(CurrentAgentAction.Building, buildCommand);
        }

        if (TryCanonicalGitCommand(command, out var gitCommand))
        {
            return new ActionClassification(CurrentAgentAction.Git, gitCommand);
        }

        if (TryCanonicalSearchCommand(command, out var searchCommand) ||
            values.Any(IsSearchSemanticValue))
        {
            return new ActionClassification(CurrentAgentAction.Searching, searchCommand);
        }

        return new ActionClassification(CurrentAgentAction.Running, SimplifyUnknownCommand(command));
    }

    public static bool IsTestCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var normalized = command.ToLowerInvariant();
        return normalized.Contains("dotnet test", StringComparison.Ordinal) ||
               normalized.Contains("python -m pytest", StringComparison.Ordinal) ||
               CommandTokenRegex("pytest").IsMatch(normalized) ||
               normalized.Contains("npm test", StringComparison.Ordinal) ||
               normalized.Contains("npm run test", StringComparison.Ordinal) ||
               normalized.Contains("pnpm test", StringComparison.Ordinal) ||
               normalized.Contains("yarn test", StringComparison.Ordinal) ||
               normalized.Contains("cargo test", StringComparison.Ordinal) ||
               normalized.Contains("go test", StringComparison.Ordinal) ||
               normalized.Contains("mvn test", StringComparison.Ordinal);
    }

    private static string? CanonicalTestCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var normalized = command.ToLowerInvariant();
        if (normalized.Contains("dotnet test", StringComparison.Ordinal)) return "dotnet test";
        if (normalized.Contains("python -m pytest", StringComparison.Ordinal)) return "python -m pytest";
        if (CommandTokenRegex("pytest").IsMatch(normalized)) return "pytest";
        if (normalized.Contains("npm run test", StringComparison.Ordinal)) return "npm run test";
        if (normalized.Contains("npm test", StringComparison.Ordinal)) return "npm test";
        if (normalized.Contains("pnpm test", StringComparison.Ordinal)) return "pnpm test";
        if (normalized.Contains("yarn test", StringComparison.Ordinal)) return "yarn test";
        if (normalized.Contains("cargo test", StringComparison.Ordinal)) return "cargo test";
        if (normalized.Contains("go test", StringComparison.Ordinal)) return "go test";
        if (normalized.Contains("mvn test", StringComparison.Ordinal)) return "mvn test";
        return null;
    }

    private static bool TryCanonicalBuildCommand(string? command, out string? summary)
    {
        summary = null;
        if (string.IsNullOrWhiteSpace(command)) return false;

        var normalized = command.ToLowerInvariant();
        if (normalized.Contains("dotnet build", StringComparison.Ordinal)) summary = "dotnet build";
        else if (normalized.Contains("npm run build", StringComparison.Ordinal)) summary = "npm run build";
        else if (normalized.Contains("pnpm build", StringComparison.Ordinal)) summary = "pnpm build";
        else if (normalized.Contains("yarn build", StringComparison.Ordinal)) summary = "yarn build";
        else if (normalized.Contains("cargo build", StringComparison.Ordinal)) summary = "cargo build";
        else if (CommandTokenRegex("msbuild").IsMatch(normalized)) summary = "msbuild";
        else if (normalized.Contains("gradle build", StringComparison.Ordinal)) summary = "gradle build";
        return summary is not null;
    }

    private static bool TryCanonicalGitCommand(string? command, out string? summary)
    {
        summary = null;
        if (string.IsNullOrWhiteSpace(command)) return false;

        var match = GitCommandRegex().Match(command);
        if (!match.Success) return false;

        summary = $"git {match.Groups[1].Value.ToLowerInvariant()}";
        return true;
    }

    private static bool TryCanonicalSearchCommand(string? command, out string? summary)
    {
        summary = null;
        if (string.IsNullOrWhiteSpace(command)) return false;

        var match = SearchCommandRegex().Match(command);
        if (!match.Success) return false;

        summary = match.Groups[1].Value.Equals("get-childitem", StringComparison.OrdinalIgnoreCase)
            ? "Get-ChildItem"
            : match.Groups[1].Value.ToLowerInvariant();
        return true;
    }

    private static bool IsSearchSemanticValue(string value)
        => value.Contains("web_search", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("search_call", StringComparison.OrdinalIgnoreCase);

    private static string? SimplifyUnknownCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        var executableToken = ReadExecutableToken(trimmed);
        if (string.IsNullOrWhiteSpace(executableToken))
        {
            return "command …";
        }

        var executable = Path.GetFileName(executableToken);
        var summary = string.IsNullOrWhiteSpace(executable) ? "command …" : $"{executable} …";
        return summary.Length <= MaximumCommandSummaryLength
            ? summary
            : summary[..(MaximumCommandSummaryLength - 1)] + "…";
    }

    private static string? ExtractCommand(string? rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return null;
        }

        var trimmed = rawCommand.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            foreach (var name in new[] { "cmd", "command" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }

    private static string? ReadExecutableToken(string command)
    {
        var candidate = command.TrimStart();
        if (candidate.StartsWith('&'))
        {
            candidate = candidate[1..].TrimStart();
        }

        if (candidate.Length == 0)
        {
            return null;
        }

        if (candidate[0] is '\'' or '"')
        {
            var quote = candidate[0];
            var closing = candidate.IndexOf(quote, 1);
            return closing > 1 ? candidate[1..closing] : null;
        }

        var separator = candidate.IndexOfAny([' ', '\t', '\r', '\n']);
        return separator < 0 ? candidate : candidate[..separator];
    }

    [GeneratedRegex(@"(?i)(?:^|[\s;&|])git(?:\.exe)?\s+(status|diff|commit|push|pull|fetch|log|branch|checkout|switch|merge|rebase)(?:\s|$)")]
    private static partial Regex GitCommandRegex();

    [GeneratedRegex(@"(?i)(?:^|[\s;&|])(rg|grep|find|get-childitem|select-string)(?:\.exe)?(?:\s|$)")]
    private static partial Regex SearchCommandRegex();

    private static Regex CommandTokenRegex(string command)
        => new($@"(?i)(?:^|[\s;&|]){Regex.Escape(command)}(?:\.exe)?(?:\s|$)", RegexOptions.CultureInvariant);
}

public sealed record ActionClassification(CurrentAgentAction Action, string? CommandSummary = null);
