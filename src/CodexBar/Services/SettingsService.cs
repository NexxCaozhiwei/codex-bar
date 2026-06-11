using System.IO;
using System.Text.Json;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class SettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _appDataRoot;
    private readonly string _localAppDataRoot;

    public SettingsService(ILogger<SettingsService> logger, string? appDataRoot = null, string? localAppDataRoot = null)
    {
        _logger = logger;
        _appDataRoot = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _localAppDataRoot = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public string SettingsPath => Path.Combine(
        _appDataRoot,
        "CodexBar",
        "settings.json");

    public string StatePath => Path.Combine(
        _appDataRoot,
        "CodexBar",
        "state.json");

    public string LogDirectory => Path.Combine(
        _localAppDataRoot,
        "CodexBar",
        "logs");

    public AppSettings Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        Directory.CreateDirectory(LogDirectory);

        if (!File.Exists(SettingsPath))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "settings.json 已损坏，正在恢复默认设置。");
            var backup = SettingsPath + ".bad-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Move(SettingsPath, backup, overwrite: true);
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
    }
}
