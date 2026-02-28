using System.IO;
using System.Text.Json;
using LumiControl.Core.Models;
using Serilog;

namespace LumiControl.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync();
    Task SaveAsync();
}

public class SettingsService : ISettingsService
{
    private readonly ILogger _logger;
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(ILogger logger)
    {
        _logger = logger.ForContext<SettingsService>();
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LumiControl");
        Directory.CreateDirectory(appData);
        _filePath = Path.Combine(appData, "settings.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _logger.Information("Settings loaded from {Path}", _filePath);
            }
            else
            {
                _logger.Information("No settings file found, using defaults");
                Settings = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings, using defaults");
            Settings = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
            _logger.Information("Settings saved");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
        }
    }
}
