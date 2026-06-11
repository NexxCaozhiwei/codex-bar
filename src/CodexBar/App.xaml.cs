using System.Windows;
using CodexBar.Services;
using CodexBar.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodexBar;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<JsonQuotaParser>();
                services.AddSingleton<CodexLocator>();
                services.AddSingleton<CodexAppServerClient>();
                services.AddSingleton<CodexSessionLogReader>();
                services.AddSingleton<CodexActivityDetector>();
                services.AddSingleton<QuotaService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<StartupService>();
                services.AddSingleton<WindowDockingService>();
                services.AddSingleton<TrayService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        var tray = _host.Services.GetRequiredService<TrayService>();
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();

        tray.Initialize(window, viewModel);
        window.Show();
        viewModel.ApplyWindowPlacement();
        ShowMainWindow(window, viewModel.Settings.TopMost);
        await viewModel.RefreshAsync();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        _host.Services.GetRequiredService<TrayService>().Dispose();
        _host.Services.GetRequiredService<CodexAppServerClient>().Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(2));
        _host.Dispose();
    }

    private static void ShowMainWindow(Window window, bool keepTopmost)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = keepTopmost;
        window.Focus();
    }
}
