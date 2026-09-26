using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodexBar.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void CorruptedSettingsFallsBackToDefaults()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));
        var service = new SettingsService(NullLogger<SettingsService>.Instance, temp);
        Directory.CreateDirectory(Path.GetDirectoryName(service.SettingsPath)!);
        File.WriteAllText(service.SettingsPath, "{ not json");

        var settings = service.Load();

        Assert.True(settings.TopMost);
        Assert.True(File.Exists(service.SettingsPath));
        Directory.Delete(temp, recursive: true);
    }

    [Fact]
    public void OlderSettingsKeepNotificationDefaults()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));
        var service = new SettingsService(NullLogger<SettingsService>.Instance, temp);
        Directory.CreateDirectory(Path.GetDirectoryName(service.SettingsPath)!);
        File.WriteAllText(service.SettingsPath, "{\"topMost\":false}");

        var settings = service.Load();

        Assert.False(settings.TopMost);
        Assert.True(settings.NotifyOnTaskCompleted);
        Assert.True(settings.NotifyOnWaitingUser);
        Assert.True(settings.NotifyOnWaitingApproval);
        Assert.True(settings.NotifyOnError);
        Assert.Equal(30, settings.NotificationMinimumTaskDurationSeconds);
        Assert.Equal(CodexBar.Models.UpdateMode.Notify, settings.UpdateMode);
        Directory.Delete(temp, recursive: true);
    }

    [Fact]
    public void UpdateSettingsRoundTripAsReadableValues()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));
        var service = new SettingsService(NullLogger<SettingsService>.Instance, temp);
        var settings = new CodexBar.Models.AppSettings
        {
            UpdateMode = CodexBar.Models.UpdateMode.AutoDownload,
            LastUpdateCheckUtc = new DateTimeOffset(2026, 9, 26, 1, 2, 3, TimeSpan.Zero)
        };

        service.Save(settings);
        var json = File.ReadAllText(service.SettingsPath);
        var loaded = service.Load();

        Assert.Contains("\"updateMode\": \"AutoDownload\"", json);
        Assert.Equal(CodexBar.Models.UpdateMode.AutoDownload, loaded.UpdateMode);
        Assert.Equal(settings.LastUpdateCheckUtc, loaded.LastUpdateCheckUtc);
        Directory.Delete(temp, recursive: true);
    }
}
