using System.IO;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class CodexLocator
{
    private readonly ILogger<CodexLocator> _logger;
    private readonly Func<string, CancellationToken, Task<string?>> _whereAsync;
    private readonly IEnumerable<string>? _commonPaths;

    public CodexLocator(
        ILogger<CodexLocator> logger,
        Func<string, CancellationToken, Task<string?>>? whereAsync = null,
        IEnumerable<string>? commonPaths = null)
    {
        _logger = logger;
        _whereAsync = whereAsync ?? WhereAsync;
        _commonPaths = commonPaths;
    }

    public async Task<CodexLocationResult> LocateAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        if (IsExecutableCandidate(configuredPath))
        {
            return new CodexLocationResult(configuredPath, null);
        }

        foreach (var command in new[] { "codex", "codex.cmd", "codex.exe" })
        {
            var located = await _whereAsync(command, cancellationToken).ConfigureAwait(false);
            if (IsExecutableCandidate(located))
            {
                return new CodexLocationResult(located, null);
            }
        }

        foreach (var candidate in _commonPaths ?? EnumerateCommonPaths())
        {
            if (IsExecutableCandidate(candidate))
            {
                return new CodexLocationResult(candidate, null);
            }
        }

        const string error = "Codex CLI was not found. Install Codex CLI or set the command path in Codex Bar settings.";
        _logger.LogWarning(error);
        return new CodexLocationResult(null, error);
    }

    private static async Task<string?> WhereAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCommonPaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var npmRoaming = Path.Combine(appData, "npm");
        var npmLocal = Path.Combine(localAppData, "npm");

        foreach (var root in new[] { npmRoaming, npmLocal, Path.Combine(userProfile, ".npm-global", "bin") })
        {
            yield return Path.Combine(root, "codex.cmd");
            yield return Path.Combine(root, "codex.exe");
            yield return Path.Combine(root, "codex");
        }
    }

    private static bool IsExecutableCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return File.Exists(Environment.ExpandEnvironmentVariables(path));
    }
}
