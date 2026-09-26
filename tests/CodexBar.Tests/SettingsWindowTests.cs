using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using CodexBar.Services;
using CodexBar.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class SettingsWindowTests
{
    [Fact]
    public void SettingsWindowCanShowWithReadOnlyUpdateProgress()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));
            SettingsWindow? window = null;
            try
            {
                Velopack.VelopackApp.Build().SetArgs([]).SetAutoApplyOnStartup(false).Run();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton(new SettingsService(NullLogger<SettingsService>.Instance, temp, temp));
                services.AddSingleton<JsonQuotaParser>();
                services.AddSingleton<CodexLocator>();
                services.AddSingleton<CodexAppServerClient>();
                services.AddSingleton<CodexSessionLogReader>();
                services.AddSingleton<CodexActivityReducer>();
                services.AddSingleton<ActionClassifier>();
                services.AddSingleton<IGitRootLocator, GitRootLocator>();
                services.AddSingleton<IProjectResolver, ProjectResolver>();
                services.AddSingleton<CodexActivityDetector>();
                services.AddSingleton<ActivityNotificationService>();
                services.AddSingleton<QuotaService>();
                services.AddSingleton<StartupService>();
                services.AddSingleton<WindowDockingService>();
                services.AddSingleton<TrayService>();
                services.AddSingleton<UpdateService>();
                services.AddSingleton<MainViewModel>();
                using var provider = services.BuildServiceProvider();
                window = new SettingsWindow(provider.GetRequiredService<MainViewModel>());

                window.Show();
                window.UpdateLayout();

                Assert.True(window.IsVisible);
                Assert.Equal(3, ((ComboBox)window.FindName("UpdateModeBox")).Items.Count);
                Assert.Equal(2, ((ComboBox)window.FindName("LanguageBox")).Items.Count);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "设置窗口初始化超时。");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
