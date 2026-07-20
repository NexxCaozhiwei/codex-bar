using System.Windows;
using System.Windows.Threading;
using CodexBar.Models;
using CodexBar.Services;
using Brush = System.Windows.Media.Brush;

namespace CodexBar.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly QuotaService _quotaService;
    private readonly CodexActivityDetector _activityDetector;
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService;
    private readonly WindowDockingService _dockingService;
    private readonly TrayService _trayService;
    private readonly DispatcherTimer _timer = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Window? _mainWindow;
    private QuotaSnapshot _quota = QuotaSnapshot.Empty("尚未刷新。");
    private CodexActivitySnapshot _activity = new(CodexActivityStatus.Idle, DateTimeOffset.Now, "尚未刷新。");
    private QuotaDisplayMode _quotaDisplayMode = QuotaDisplayMode.Remaining;
    private bool _isRefreshing;

    public MainViewModel(
        QuotaService quotaService,
        CodexActivityDetector activityDetector,
        SettingsService settingsService,
        StartupService startupService,
        WindowDockingService dockingService,
        TrayService trayService)
    {
        _quotaService = quotaService;
        _activityDetector = activityDetector;
        _settingsService = settingsService;
        _startupService = startupService;
        _dockingService = dockingService;
        _trayService = trayService;
        Settings = _settingsService.Load();
        Settings.StartWithWindows = _startupService.IsEnabled();
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsRefreshing);
        SettingsCommand = new RelayCommand(() => ShowSettings());
        DetailsCommand = new RelayCommand(() => ShowDetails());

        ConfigureTimer();
    }

    public AppSettings Settings { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SettingsCommand { get; }

    public RelayCommand DetailsCommand { get; }

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

    public string DetailStatusText => _activity.Detail;

    public Brush StatusBrush => ActivityStatusPalette.StatusBrush(_activity.Status);

    public Brush GreenLightBrush => ActivityStatusPalette.GreenLightBrush(_activity.Status);

    public Brush BlueLightBrush => ActivityStatusPalette.BlueLightBrush(_activity.Status);

    public Brush RedLightBrush => ActivityStatusPalette.RedLightBrush(_activity.Status);

    public double FiveHourRemaining => _quota.FiveHour?.RemainingPercent ?? 0;

    public double WeeklyRemaining => _quota.Weekly?.RemainingPercent ?? 0;

    public string FiveHourText => QuotaDisplayFormatter.FormatSummary(_quota.FiveHour, _quotaDisplayMode, DateTimeOffset.Now);

    public string WeeklyText => QuotaDisplayFormatter.FormatSummary(_quota.Weekly, _quotaDisplayMode, DateTimeOffset.Now);

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
                    QuotaDisplayFormatter.FormatSummary(window, _quotaDisplayMode, DateTimeOffset.Now),
                    FormatQuotaDetails(window)))
                .ToArray();

            if (rows.Length > 0)
            {
                return rows;
            }

            var summary = _quota.Credits?.Unlimited == true ? "无限" : "暂无窗口";
            return [new QuotaDisplayRow("额度", _quota.Credits?.Unlimited == true ? 100 : 0, summary, summary)];
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
        ConfigureTimer();
    }

    public void ApplyWindowPlacement()
    {
        if (_mainWindow is not null)
        {
            _dockingService.Apply(_mainWindow, Settings);
        }
    }

    public async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            _quota = await _quotaService.ReadAsync(Settings);
            _activity = await _activityDetector.DetectAsync();
            RaiseAllDisplayProperties();
            var primaryWindow = DisplayWindows().FirstOrDefault();
            var quotaText = primaryWindow is null
                ? (_quota.Credits?.Unlimited == true ? "额度无限" : "额度不可用")
                : $"{primaryWindow.Label} 剩余 {primaryWindow.RemainingPercent:0}%";
            _trayService.UpdateText($"Codex Bar - {StatusText} - {quotaText}");
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
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
        _settingsService.Save(Settings);
        _startupService.SetEnabled(Settings.StartWithWindows);
        ConfigureTimer();
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

    private void ConfigureTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.RefreshIntervalSeconds, 5, 3600));
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

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
        return $"{window.Label}：已用 {window.UsedPercent:0.##}%，剩余 {window.RemainingPercent:0.##}%，重置时间 {reset}";
    }

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
        CodexActivityStatus.Working => "正在工作",
        CodexActivityStatus.WaitingForUser => "等待用户",
        CodexActivityStatus.AutoReviewing => "自动审查",
        CodexActivityStatus.Completed => "已完成",
        CodexActivityStatus.Unknown => "未知",
        CodexActivityStatus.Error => "错误",
        _ => "未知"
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
    string Details);
