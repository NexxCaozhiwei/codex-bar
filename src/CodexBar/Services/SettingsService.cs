using System.IO;
using System.Text.Json;
using CodexBar.Models;
using Microsoft.Extensions.Logging;

namespace CodexBar.Services;

public sealed class SettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();
    private readonly string _appDataRoot;

    public SettingsService(ILogger<SettingsService> logger, string? appDataRoot = null)
    {
        _logger = logger;
        _appDataRoot = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public string SettingsPath => Path.Combine(
        _appDataRoot,
        "CodexBar",
        "settings.json");

    public AppSettings Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }
}
