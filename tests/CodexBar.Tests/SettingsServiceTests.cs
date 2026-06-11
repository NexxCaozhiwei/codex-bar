using CodexBar.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexBar.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void CorruptedSettingsFallsBackToDefaults()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CodexBar.Tests", Guid.NewGuid().ToString("N"));
        var service = new SettingsService(NullLogger<SettingsService>.Instance, temp, temp);
        Directory.CreateDirectory(Path.GetDirectoryName(service.SettingsPath)!);
        File.WriteAllText(service.SettingsPath, "{ not json");

        var settings = service.Load();

        Assert.True(settings.TopMost);
        Assert.True(File.Exists(service.SettingsPath));
        Directory.Delete(temp, recursive: true);
    }
}
