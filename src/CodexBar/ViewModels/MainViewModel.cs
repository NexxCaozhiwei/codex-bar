using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using CodexBar.Models;
using CodexBar.Services;
using Brush = System.Windows.Media.Brush;
using Velopack;
using WpfApplication = System.Windows.Application;

namespace CodexBar.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ActivityRefreshInterval = TimeSpan.FromSeconds(3);

    private readonly QuotaService _quotaService;
    private readonly CodexActivityDetector _activityDetector;
    private readonly ActivityNotificationService _notificationService;
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly WindowDockingService _dockingService;
    private readonly TrayService _trayService;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _quotaTimer = new();
    private readonly DispatcherTimer _activityTimer = new();
    private readonly DispatcherTimer _durationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _updateStartupTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _updateDailyTimer = new() { Interval = TimeSpan.FromDays(1) };
    private readonly SemaphoreSlim _manualRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _quotaRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _activityRefreshGate = new(1, 1);
    private Window? _mainWindow;
    private QuotaSnapshot _quota = QuotaSnapshot.Empty("尚未刷新。");
    private CodexActivitySnapshot _activity = new(CodexActivityStatus.Idle, DateTimeOffset.Now, "尚未刷新。");
    private CurrentAgentContext _agentContext;
    private QuotaDisplayMode _quotaDisplayMode = QuotaDisplayMode.Remaining;
    private bool _isRefreshing;
    private bool _windowPlacementApplied;
    private bool _isUpdateBusy;
    private int _updateProgress;
    private string _updateStatusText = "尚未检查更新。";
    private UpdateInfo? _availableUpdate;

    public MainViewModel(
        QuotaService quotaService,
        CodexActivityDetector activityDetector,
        ActivityNotificationService notificationService,
        SettingsService settingsService,
        StartupService startupService,
        WindowDockingService dockingService,
        TrayService trayService,
        UpdateService updateService)
    {
        _quotaService = quotaService;
        _activityDetector = activityDetector;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _startupService = startupService;
        _dockingService = dockingService;
        _trayService = trayService;
        _updateService = updateService;
        _agentContext = CurrentAgentContext.Empty(_activity);
        Settings = _settingsService.Load();
        Settings.StartWithWindows = _startupService.IsEnabled();
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
        SettingsCommand = new RelayCommand(() => ShowSettings());
        DetailsCommand = new RelayCommand(() => ShowDetails());
        CheckUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => !IsUpdateBusy);
        DownloadUpdateCommand = new RelayCommand(async _ => await DownloadUpdateAsync(), _ => !IsUpdateBusy && HasUpdateAvailable);
        ApplyUpdateCommand = new RelayCommand(_ => ApplyDownloadedUpdate(), _ => !IsUpdateBusy && HasPendingUpdate);
        OpenReleasePageCommand = new RelayCommand(OpenLatestReleasePage);
        _updateStatusText = InitialUpdateStatus();
        _updateStartupTimer.Tick += OnUpdateStartupTimerTick;
        _updateDailyTimer.Tick += OnUpdateDailyTimerTick;

        ConfigureTimers();
        _durationTimer.Tick += (_, _) =>
        {
            RaisePropertyChanged(nameof(StatusDurationText));
            RaisePropertyChanged(nameof(StatusDurationShortText));
            RaisePropertyChanged(nameof(StatusSummaryText));
            RaisePropertyChanged(nameof(FiveHourText));
            RaisePropertyChanged(nameof(WeeklyText));
            RaisePropertyChanged(nameof(FiveHourDetails));
            RaisePropertyChanged(nameof(WeeklyDetails));
            RaisePropertyChanged(nameof(QuotaRows));
        };
        _durationTimer.Start();
    }

    public AppSettings Settings { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand DetailsCommand { get; }

    public RelayCommand CheckUpdatesCommand { get; }

    public RelayCommand DownloadUpdateCommand { get; }

    public RelayCommand ApplyUpdateCommand { get; }

    public RelayCommand OpenReleasePageCommand { get; }

    public string CurrentVersionText => _updateService.CurrentVersion;

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, value);
    }

    public bool IsUpdateBusy
    {
        get => _isUpdateBusy;
        private set
        {
            if (SetProperty(ref _isUpdateBusy, value))
            {
                RaiseUpdateCommandStates();
            }
        }
    }

    public bool HasUpdateAvailable => _availableUpdate is not null;

    public bool HasPendingUpdate => _updateService.HasPendingRestart;

    public string AvailableVersionText => _availableUpdate?.TargetFullRelease.Version.ToString() ?? "";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText => FormatStatus(_activity.Status);

    public string StatusSummaryText
    {
        get
        {
            if (_activity.Status == CodexActivityStatus.Idle)
            {
                return "Idle";
            }

            var project = string.IsNullOrWhiteSpace(_agentContext.ProjectName)
                ? null
                : _agentContext.ProjectName;
            return project is null
                ? $"{CurrentActionText}  {StatusDurationShortText}"
                : $"{project} · {CurrentActionText}  {StatusDurationShortText}";
        }
    }

    public string StatusDurationText => FormatDuration(StateDuration);

    public string StatusDurationShortText => _activity.Status == CodexActivityStatus.Idle
        ? ""
        : FormatCompactDuration(StateDuration);

    public string ProjectNameText => _agentContext.ProjectName ?? "未识别";

    public string ProjectDisplayText => _activity.Status == CodexActivityStatus.Idle ||
                                        string.IsNullOrWhiteSpace(_agentContext.ProjectName)
        ? ""
        : $"{_agentContext.ProjectName} ·";

    public string CurrentActionText => _agentContext.CurrentAction.DisplayName();

    public string CurrentCommandText => _agentContext.CurrentCommand ?? "无";

    public string WorkingDirectoryText => _agentContext.WorkingDirectory ?? "未识别";

    public string CurrentSessionText => FormatSessionId(_agentContext.SessionId);

    public string ActivityStateText => FormatActivityState(_activity.Status);

    public string DetailStatusText => _activity.Detail;

    public Brush StatusBrush => ActivityStatusPalette.StatusBrush(_activity.Status);

    public Brush GreenLightBrush => ActivityStatusPalette.GreenLightBrush(_activity.Status);

    public Brush BlueLightBrush => ActivityStatusPalette.BlueLightBrush(_activity.Status);

    public Brush RedLightBrush => ActivityStatusPalette.RedLightBrush(_activity.Status);

    public double FiveHourRemaining => _quota.FiveHour?.RemainingPercent ?? 0;

    public double WeeklyRemaining => _quota.Weekly?.RemainingPercent ?? 0;

    public string FiveHourText => FormatQuotaSummary(_quota.FiveHour);

    public string WeeklyText => FormatQuotaSummary(_quota.Weekly);

    public string FiveHourDetails => FormatQuotaDetails(_quota.FiveHour);

    public string WeeklyDetails => FormatQuotaDetails(_quota.Weekly);

    public IReadOnlyList<QuotaDisplayRow> QuotaRows
    {
        get
        {
            var rows = DisplayWindows()
                .Select(window => new QuotaDisplayRow(
                    window.Label,
                    window.RemainingPercent,
                    IsQuotaWindowStale(window)
                        ? "已过期"
                        : QuotaDisplayFormatter.FormatSummary(window, _quotaDisplayMode, DateTimeOffset.Now),
                    FormatQuotaDetails(window),
                    IsQuotaWindowStale(window)))
                .ToArray();

            if (rows.Length > 0)
            {
                return rows;
            }

            var summary = _quota.Credits?.Unlimited == true ? "无限" : "暂无窗口";
            return [new QuotaDisplayRow("额度", _quota.Credits?.Unlimited == true ? 100 : 0, summary, summary, false)];
        }
    }

    public string QuotaAccountDetails
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_quota.PlanType))
            {
                parts.Add($"方案：{_quota.PlanType}");
            }

            if (_quota.Credits is { } credits)
            {
                parts.Add(credits.Unlimited
                    ? "追加额度：无限"
                    : $"追加额度：{(credits.HasCredits ? credits.Balance ?? "可用" : "不可用")}");
            }

            if (_quota.AvailableResetCredits is { } resetCredits)
            {
                parts.Add($"额度重置券：{resetCredits}");
            }

            if (!string.IsNullOrWhiteSpace(_quota.RateLimitReachedType))
            {
                parts.Add($"限制状态：{_quota.RateLimitReachedType}");
            }

            return parts.Count == 0 ? "额度账户信息：暂无" : string.Join("；", parts);
        }
    }

    public string LastRefreshText => _quota.LastRefresh.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string CodexPathText => _quotaService.LastLocation.Path ?? _quotaService.LastLocation.Error ?? "未知";

    public string DataSourceText => FormatDataSource(_quota.Source);

    public string ErrorText
    {
        get
        {
            var errors = new[] { _activity.Status == CodexActivityStatus.Error ? _activity.Detail : null, _quota.Error }
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct()
                .ToArray();
            return errors.Length == 0 ? "" : string.Join(Environment.NewLine, errors);
        }
    }

    public void AttachWindow(Window window)
    {
        _mainWindow = window;
        ApplyWindowVisuals();
        ConfigureTimers();
    }

    public void ApplyWindowPlacement()
    {
        if (_mainWindow is not null)
        {
            _dockingService.Apply(_mainWindow, Settings);
            _windowPlacementApplied = true;
        }
    }

    public void OnMainWindowSizeChanged()
    {
        if (_windowPlacementApplied && Settings.AutoDockToTaskbar && _mainWindow is not null)
        {
            _dockingService.DockNearTaskbar(_mainWindow);
        }
    }

    public async Task RefreshAsync()
    {
        if (!await _manualRefreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            await Task.WhenAll(RefreshQuotaAsync(), RefreshActivityAsync());
        }
        finally
        {
            IsRefreshing = false;
            _manualRefreshGate.Release();
        }
    }

    private async Task RefreshQuotaAsync()
    {
        if (!await _quotaRefreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            _quota = await _quotaService.ReadAsync(Settings);
            RaiseAllDisplayProperties();
            UpdateTrayText();
        }
        finally
        {
            _quotaRefreshGate.Release();
        }
    }

    private async Task RefreshActivityAsync()
    {
        if (!await _activityRefreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            _agentContext = await _activityDetector.DetectContextAsync();
            _activity = _agentContext.Activity;
            var notification = _notificationService.Observe(_activity, Settings);
            RaiseAllDisplayProperties();
            UpdateTrayText();
            if (notification is not null)
            {
                _trayService.ShowNotification(notification);
            }
        }
        finally
        {
            _activityRefreshGate.Release();
        }
    }

    private void UpdateTrayText()
    {
        var primaryWindow = DisplayWindows().FirstOrDefault();
        var quotaText = primaryWindow is null
            ? (_quota.Credits?.Unlimited == true ? "额度无限" : "额度不可用")
            : IsQuotaWindowStale(primaryWindow)
                ? $"{primaryWindow.Label} 已过期"
                : $"{primaryWindow.Label} 剩余 {primaryWindow.RemainingPercent:0}%";
        _trayService.UpdateText($"Codex Bar - {StatusSummaryText} - {quotaText}");
    }

    public void ShowDetails()
    {
        var details = new DetailsWindow { DataContext = this, Owner = _mainWindow };
        details.Show();
    }

    public void ShowSettings()
    {
        var settings = new SettingsWindow(this) { Owner = _mainWindow };
        settings.ShowDialog();
    }

    public void StartUpdateChecks()
    {
        if (Settings.UpdateMode != UpdateMode.Manual && _updateService.IsManagedInstall)
        {
            _updateStartupTimer.Start();
        }
    }

    public async Task CheckForUpdatesAsync(bool automatic = false)
    {
        if (IsUpdateBusy)
        {
            return;
        }

        if (!_updateService.IsManagedInstall)
        {
            UpdateStatusText = InitialUpdateStatus();
            return;
        }

        if (HasPendingUpdate)
        {
            UpdateStatusText = "更新已下载；可立即重启安装，或稍后再重启。";
            return;
        }

        IsUpdateBusy = true;
        UpdateStatusText = "正在检查更新…";
        try
        {
            _availableUpdate = await _updateService.CheckForUpdatesAsync();
            Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settingsService.Save(Settings);
            RaisePropertyChanged(nameof(HasUpdateAvailable));
            RaisePropertyChanged(nameof(AvailableVersionText));

            if (_availableUpdate is null)
            {
                UpdateStatusText = $"已是最新版本（{CurrentVersionText}）";
            }
            else
            {
                UpdateStatusText = $"发现新版本 {AvailableVersionText}";
                if (automatic && Settings.UpdateMode == UpdateMode.AutoDownload)
                {
                    await DownloadUpdateCoreAsync();
                }
                else if (automatic && Settings.UpdateMode == UpdateMode.Notify)
                {
                    _trayService.ShowUpdateNotification("发现 Codex Bar 更新", $"新版本 {AvailableVersionText} 可用，打开设置即可下载。");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            IsUpdateBusy = false;
            RaiseUpdateCommandStates();
        }
    }

    public async Task DownloadUpdateAsync()
    {
        if (!HasUpdateAvailable || IsUpdateBusy)
        {
            return;
        }

        IsUpdateBusy = true;
        try
        {
            await DownloadUpdateCoreAsync();
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"下载更新失败：{ex.Message}";
        }
        finally
        {
            IsUpdateBusy = false;
            RaiseUpdateCommandStates();
        }
    }

    public void ApplyDownloadedUpdate()
    {
        if (!HasPendingUpdate)
        {
            return;
        }

        SaveWindowPosition();
        _updateService.ApplyAndRestart();
        WpfApplication.Current.Shutdown();
    }

    public void OpenLatestReleasePage()
    {
        Process.Start(new ProcessStartInfo("https://github.com/NexxCaozhiwei/codex-bar/releases/latest")
        {
            UseShellExecute = true
        });
    }

    public void ToggleQuotaDisplayMode()
    {
        _quotaDisplayMode = _quotaDisplayMode == QuotaDisplayMode.Remaining
            ? QuotaDisplayMode.ResetCountdown
            : QuotaDisplayMode.Remaining;
        RaisePropertyChanged(nameof(FiveHourText));
        RaisePropertyChanged(nameof(WeeklyText));
        RaisePropertyChanged(nameof(QuotaRows));
    }

    public void SaveSettings()
    {
        Settings.RefreshIntervalSeconds = Math.Clamp(Settings.RefreshIntervalSeconds, 5, 3600);
        Settings.OpacityPercent = Math.Clamp(Settings.OpacityPercent, 20, 100);
        Settings.NotificationMinimumTaskDurationSeconds = Math.Clamp(
            Settings.NotificationMinimumTaskDurationSeconds,
            0,
            3600);
        _settingsService.Save(Settings);
        _startupService.SetEnabled(Settings.StartWithWindows);
        ConfigureTimers();
        ConfigureUpdateTimers();
        if (_mainWindow is not null)
        {
            ApplyWindowVisuals();
            _dockingService.Apply(_mainWindow, Settings);
        }
    }

    public void SaveWindowPosition()
    {
        if (_mainWindow is null)
        {
            return;
        }

        Settings.Left = _mainWindow.Left;
        Settings.Top = _mainWindow.Top;
        _settingsService.Save(Settings);
    }

    public void ToggleLockPosition()
    {
        Settings.LockPosition = !Settings.LockPosition;
        SaveSettings();
    }

    public void ToggleTopMost()
    {
        Settings.TopMost = !Settings.TopMost;
        SaveSettings();
    }

    public void ToggleStartup()
    {
        Settings.StartWithWindows = !Settings.StartWithWindows;
        SaveSettings();
    }

    public void DockNow()
    {
        if (_mainWindow is not null)
        {
            _dockingService.DockNearTaskbar(_mainWindow);
        }
    }

    private void ConfigureTimers()
    {
        _quotaTimer.Stop();
        _quotaTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.RefreshIntervalSeconds, 5, 3600));
        _quotaTimer.Tick -= OnQuotaTimerTick;
        _quotaTimer.Tick += OnQuotaTimerTick;
        _quotaTimer.Start();

        _activityTimer.Stop();
        _activityTimer.Interval = ActivityRefreshInterval;
        _activityTimer.Tick -= OnActivityTimerTick;
        _activityTimer.Tick += OnActivityTimerTick;
        _activityTimer.Start();
    }

    private void ConfigureUpdateTimers()
    {
        _updateStartupTimer.Stop();
        _updateDailyTimer.Stop();
        if (Settings.UpdateMode != UpdateMode.Manual && _updateService.IsManagedInstall)
        {
            _updateStartupTimer.Start();
        }
    }

    private async void OnUpdateStartupTimerTick(object? sender, EventArgs e)
    {
        _updateStartupTimer.Stop();
        if (Settings.UpdateMode == UpdateMode.Manual)
        {
            return;
        }

        if (Settings.LastUpdateCheckUtc is null || DateTimeOffset.UtcNow - Settings.LastUpdateCheckUtc >= TimeSpan.FromHours(24))
        {
            await CheckForUpdatesAsync(automatic: true);
        }

        _updateDailyTimer.Start();
    }

    private async void OnUpdateDailyTimerTick(object? sender, EventArgs e)
    {
        if (Settings.UpdateMode != UpdateMode.Manual)
        {
            await CheckForUpdatesAsync(automatic: true);
        }
    }

    private async Task DownloadUpdateCoreAsync()
    {
        if (_availableUpdate is null)
        {
            return;
        }

        UpdateProgress = 0;
        UpdateStatusText = $"正在下载 {AvailableVersionText}…";
        await _updateService.DownloadUpdatesAsync(_availableUpdate, progress =>
        {
            WpfApplication.Current.Dispatcher.BeginInvoke(() => UpdateProgress = progress);
        });
        _availableUpdate = null;
        RaisePropertyChanged(nameof(HasUpdateAvailable));
        RaisePropertyChanged(nameof(AvailableVersionText));
        RaisePropertyChanged(nameof(HasPendingUpdate));
        ApplyUpdateCommand.RaiseCanExecuteChanged();
        UpdateProgress = 100;
        UpdateStatusText = "更新已下载；可立即重启安装，或稍后再重启。";
        _trayService.ShowUpdateNotification("Codex Bar 更新已就绪", "重启应用即可完成更新。");
    }

    private string InitialUpdateStatus() => !_updateService.IsManagedInstall
        ? "当前为旧版 ZIP 安装；首次使用自动更新前，请先下载支持更新的便携版并手动迁移。"
        : _updateService.HasPendingRestart ? "更新已下载；重启应用即可完成更新。" : "尚未检查更新。";

    private void RaiseUpdateCommandStates()
    {
        CheckUpdatesCommand.RaiseCanExecuteChanged();
        DownloadUpdateCommand.RaiseCanExecuteChanged();
        ApplyUpdateCommand.RaiseCanExecuteChanged();
    }

    private async void OnQuotaTimerTick(object? sender, EventArgs e) => await RefreshQuotaAsync();

    private async void OnActivityTimerTick(object? sender, EventArgs e) => await RefreshActivityAsync();

    private void ApplyWindowVisuals()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Opacity = Math.Clamp(Settings.OpacityPercent, 20, 100) / 100d;
        }
    }

    private void RaiseAllDisplayProperties()
    {
        foreach (var property in new[]
        {
            nameof(StatusText),
            nameof(StatusSummaryText),
            nameof(StatusDurationText),
            nameof(StatusDurationShortText),
            nameof(ProjectNameText),
            nameof(ProjectDisplayText),
            nameof(CurrentActionText),
            nameof(CurrentCommandText),
            nameof(WorkingDirectoryText),
            nameof(CurrentSessionText),
            nameof(ActivityStateText),
            nameof(DetailStatusText),
            nameof(StatusBrush),
            nameof(RedLightBrush),
            nameof(GreenLightBrush),
            nameof(BlueLightBrush),
            nameof(FiveHourRemaining),
            nameof(WeeklyRemaining),
            nameof(FiveHourText),
            nameof(WeeklyText),
            nameof(FiveHourDetails),
            nameof(WeeklyDetails),
            nameof(QuotaRows),
            nameof(QuotaAccountDetails),
            nameof(LastRefreshText),
            nameof(CodexPathText),
            nameof(DataSourceText),
            nameof(ErrorText)
        })
        {
            RaisePropertyChanged(property);
        }
    }

    private static string FormatQuotaDetails(QuotaWindow? window)
    {
        if (window is null)
        {
            return "暂无数据";
        }

        var reset = window.ResetsAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
        var stale = IsQuotaWindowStale(window)
            ? "已过期，以下为上次读取值，等待刷新确认；"
            : "";
        return $"{window.Label}：{stale}已用 {window.UsedPercent:0.##}%，剩余 {window.RemainingPercent:0.##}%，重置时间 {reset}";
    }

    private string FormatQuotaSummary(QuotaWindow? window)
        => window is null
            ? QuotaDisplayFormatter.FormatSummary(null, _quotaDisplayMode, DateTimeOffset.Now)
            : IsQuotaWindowStale(window)
                ? "已过期"
                : QuotaDisplayFormatter.FormatSummary(window, _quotaDisplayMode, DateTimeOffset.Now);

    private static bool IsQuotaWindowStale(QuotaWindow window)
        => window.IsStale || window.ResetsAt is { } resetsAt && resetsAt <= DateTimeOffset.Now;

    private IReadOnlyList<QuotaWindow> DisplayWindows()
    {
        if (_quota.Windows.Count > 0)
        {
            return _quota.Windows;
        }

        return new[] { _quota.FiveHour, _quota.Weekly }
            .Where(window => window is not null)
            .Cast<QuotaWindow>()
            .DistinctBy(window => (window.WindowDurationMins, window.LimitId))
            .OrderBy(window => window.WindowDurationMins)
            .ToArray();
    }

    private static string FormatStatus(CodexActivityStatus status) => status switch
    {
        CodexActivityStatus.Idle => "空闲",
        CodexActivityStatus.Thinking => "思考中",
        CodexActivityStatus.Editing => "编辑中",
        CodexActivityStatus.RunningCommand => "运行命令",
        CodexActivityStatus.RunningTests => "运行测试",
        CodexActivityStatus.Reviewing => "审查中",
        CodexActivityStatus.WaitingApproval => "等待审批",
        CodexActivityStatus.WaitingUser => "等待用户",
        CodexActivityStatus.Completed => "已完成",
        CodexActivityStatus.Unknown => "未知",
        CodexActivityStatus.Error => "错误",
        _ => "未知"
    };

    private TimeSpan StateDuration
    {
        get
        {
            var duration = DateTimeOffset.Now - _agentContext.StartedAt;
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }
    }

    private static string FormatCompactDuration(TimeSpan duration)
        => duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{(int)duration.TotalSeconds} 秒";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒";
    }

    private static string FormatSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return "未识别";
        }

        var trimmed = sessionId.Trim();
        return trimmed.Length <= 12 ? trimmed : trimmed[..8] + "…";
    }

    private static string FormatActivityState(CodexActivityStatus status) => status switch
    {
        CodexActivityStatus.RunningCommand => "Running Command",
        CodexActivityStatus.RunningTests => "Running Tests",
        CodexActivityStatus.WaitingApproval => "Waiting Approval",
        CodexActivityStatus.WaitingUser => "Waiting User",
        _ => status.ToString()
    };

    private static string FormatDataSource(QuotaDataSource source) => source switch
    {
        QuotaDataSource.AppServer => "app-server",
        QuotaDataSource.JsonlFallback => "jsonl 回退",
        _ => "无数据"
    };
}

public sealed record QuotaDisplayRow(
    string Label,
    double RemainingPercent,
    string Summary,
    string Details,
    bool IsStale);
