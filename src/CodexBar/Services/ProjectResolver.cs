using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexBar.Services;

public interface IProjectResolver
{
    Task<ProjectResolution> ResolveAsync(string? workingDirectory, CancellationToken cancellationToken = default);
}

public sealed class ProjectResolver : IProjectResolver
{
    private readonly IGitRootLocator _gitRootLocator;
    private readonly ConcurrentDictionary<string, Lazy<Task<ProjectResolution>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public ProjectResolver(IGitRootLocator gitRootLocator)
    {
        _gitRootLocator = gitRootLocator;
    }

    public async Task<ProjectResolution> ResolveAsync(
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeExistingDirectory(workingDirectory);
        if (normalized is null)
        {
            return ProjectResolution.Empty;
        }

        var lazy = _cache.GetOrAdd(
            normalized,
            path => new Lazy<Task<ProjectResolution>>(
                () => ResolveCoreAsync(path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProjectResolution> ResolveCoreAsync(string workingDirectory)
    {
        var gitRoot = await _gitRootLocator.FindRootAsync(workingDirectory).ConfigureAwait(false);
        var projectDirectory = NormalizeExistingDirectory(gitRoot) ?? workingDirectory;
        var projectName = DirectoryName(projectDirectory);
        return string.IsNullOrWhiteSpace(projectName)
            ? new ProjectResolution(null, workingDirectory, gitRoot)
            : new ProjectResolution(projectName, workingDirectory, gitRoot);
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? DirectoryName(string directory)
    {
        var name = new DirectoryInfo(directory).Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

public sealed record ProjectResolution(
    string? ProjectName,
    string? WorkingDirectory,
    string? GitRoot)
{
    public static ProjectResolution Empty { get; } = new(null, null, null);
}

public interface IGitRootLocator
{
    Task<string?> FindRootAsync(string workingDirectory, CancellationToken cancellationToken = default);
}

public sealed class GitRootLocator : IGitRootLocator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<string?> FindRootAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(workingDirectory);
        process.StartInfo.ArgumentList.Add("rev-parse");
        process.StartInfo.ArgumentList.Add("--show-toplevel");

        try
        {
            if (!process.Start())
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = (await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false)).Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}
