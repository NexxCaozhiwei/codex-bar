using Velopack;
using Velopack.Sources;

namespace CodexBar.Services;

public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/NexxCaozhiwei/codex-bar";
    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    public bool IsManagedInstall => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ??
                                    typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "未知";

    public bool HasPendingRestart => _manager.UpdatePendingRestart is not null;

    public Task<UpdateInfo?> CheckForUpdatesAsync()
        => _manager.CheckForUpdatesAsync();

    public Task DownloadUpdatesAsync(
        UpdateInfo update,
        Action<int> progress,
        CancellationToken cancellationToken = default)
        => _manager.DownloadUpdatesAsync(update, progress, cancellationToken);

    public void ApplyAndRestart()
    {
        var pending = _manager.UpdatePendingRestart;
        if (pending is null)
        {
            return;
        }

        _manager.WaitExitThenApplyUpdates(pending, silent: false, restart: true);
    }
}
