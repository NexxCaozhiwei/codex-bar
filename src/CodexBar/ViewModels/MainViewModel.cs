using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexBar.Models;
using CodexBar.Services;

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
    private QuotaSnapshot _quota = QuotaSnapshot.Empty("Not refreshed yet.");
    private CodexActivitySnapshot _activity = new(CodexActivityStatus.Idle, DateTimeOffset.Now, "Not refreshed yet.");
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
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), _ => !IsRefreshing);
        SettingsCommand = new RelayCommand(ShowSettings);
        DetailsCommand = new RelayCommand(ShowDetails);

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

    public string StatusText => _activity.Status.ToString();

    public string DetailStatusText => _activity.Detail;

    public Brush StatusBrush => _activity.Status switch
    {
        CodexActivityStatus.Working or CodexActivityStatus.AutoReviewing => Brushes.LimeGreen,
        CodexActivityStatus.Completed => Brushes.DeepSkyBlue,
        CodexActivityStatus.WaitingForUser => Brushes.Gold,
        CodexActivityStatus.Unknown => Brushes.DarkGray,
        CodexActivityStatus.Error => Brushes.OrangeRed,
        _ => Brushes.IndianRed
    };

    public Brush RedLightBrush => _activity.Status == CodexActivityStatus.Idle ? Brushes.IndianRed : Brushes.DimGray;

    public Brush GreenLightBrush => _activity.Status is CodexActivityStatus.Working or CodexActivityStatus.AutoReviewing or CodexActivityStatus.WaitingForUser
        ? Brushes.LimeGreen
        : Brushes.DimGray;

    public Brush BlueLightBrush => _activity.Status == CodexActivityStatus.Completed ? Brushes.DeepSkyBlue : Brushes.DimGray;

    public double FiveHourRemaining => _quota.FiveHour?.RemainingPercent ?? 0;

    public double WeeklyRemaining => _quota.Weekly?.RemainingPercent ?? 0;

    public string FiveHourText => FormatQuota(_quota.FiveHour);

    public string WeeklyText => FormatQuota(_quota.Weekly);

    public string FiveHourDetails => FormatQuotaDetails(_quota.FiveHour);

    public string WeeklyDetails => FormatQuotaDetails(_quota.Weekly);

    public string LastRefreshText => _quota.LastRefresh.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string CodexPathText => _quotaService.LastLocation.Path ?? _quotaService.LastLocation.Error ?? "Unknown";

    public string DataSourceText => _quota.Source.ToString();

    public string ErrorText => _quota.Error ?? "";

    public void AttachWindow(Window window)
    {
        _mainWindow = window;
        _dockingService.Apply(window, Settings);
        ConfigureTimer();
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
            _trayService.UpdateText($"Codex Bar - {StatusText} - 5h {FiveHourRemaining:0}% left");
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

    public void SaveSettings()
    {
        Settings.RefreshIntervalSeconds = Math.Clamp(Settings.RefreshIntervalSeconds, 5, 3600);
        _settingsService.Save(Settings);
        _startupService.SetEnabled(Settings.StartWithWindows);
        ConfigureTimer();
        if (_mainWindow is not null)
        {
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
            nameof(LastRefreshText),
            nameof(CodexPathText),
            nameof(DataSourceText),
            nameof(ErrorText)
        })
        {
            RaisePropertyChanged(property);
        }
    }

    private static string FormatQuota(QuotaWindow? window)
        => window is null ? "--% left" : $"{window.Label} {window.RemainingPercent:0}% left";

    private static string FormatQuotaDetails(QuotaWindow? window)
    {
        if (window is null)
        {
            return "No data";
        }

        var reset = window.ResetsAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";
        return $"{window.Label}: used {window.UsedPercent:0.##}%, left {window.RemainingPercent:0.##}%, resets {reset}";
    }
}
