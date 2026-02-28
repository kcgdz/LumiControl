using System.Text.Json;
using LumiControl.Core.Models;
using Serilog;

namespace LumiControl.Core.Services;

public interface IProfileService
{
    Task<List<BrightnessProfile>> GetProfilesAsync();
    Task<BrightnessProfile?> GetProfileAsync(string id);
    Task SaveProfileAsync(BrightnessProfile profile);
    Task DeleteProfileAsync(string id);
    Task<BrightnessProfile> CreateFromCurrentAsync(string name, List<MonitorInfo> monitors);
    Task SetActiveProfileAsync(string? id);
    Task<string?> GetActiveProfileIdAsync();
    event EventHandler<List<BrightnessProfile>>? ProfilesChanged;
}

public class ProfileService : IProfileService
{
    private readonly string _profilesPath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public event EventHandler<List<BrightnessProfile>>? ProfilesChanged;

    public ProfileService(ILogger logger)
    {
        _logger = logger;

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LumiControl");

        Directory.CreateDirectory(appDataPath);
        _profilesPath = Path.Combine(appDataPath, "profiles.json");

        _logger.Debug("Profile store path: {Path}", _profilesPath);
    }

    public async Task<List<BrightnessProfile>> GetProfilesAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            return store.Profiles;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BrightnessProfile?> GetProfileAsync(string id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            return store.Profiles.FirstOrDefault(p => p.Id == id);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveProfileAsync(BrightnessProfile profile)
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();

            var existingIndex = store.Profiles.FindIndex(p => p.Id == profile.Id);
            if (existingIndex >= 0)
            {
                store.Profiles[existingIndex] = profile;
                _logger.Information("Updated profile '{Name}' ({Id})", profile.Name, profile.Id);
            }
            else
            {
                store.Profiles.Add(profile);
                _logger.Information("Added new profile '{Name}' ({Id})", profile.Name, profile.Id);
            }

            await PersistStoreAsync(store);
            OnProfilesChanged(store.Profiles);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteProfileAsync(string id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            var removed = store.Profiles.RemoveAll(p => p.Id == id);

            if (removed == 0)
            {
                _logger.Warning("Attempted to delete non-existent profile {Id}", id);
                return;
            }

            if (store.ActiveProfileId == id)
            {
                store.ActiveProfileId = null;
                _logger.Information("Cleared active profile because deleted profile was active");
            }

            _logger.Information("Deleted profile {Id}", id);
            await PersistStoreAsync(store);
            OnProfilesChanged(store.Profiles);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<BrightnessProfile> CreateFromCurrentAsync(string name, List<MonitorInfo> monitors)
    {
        var profile = new BrightnessProfile
        {
            Name = name,
            CreatedAt = DateTime.UtcNow,
            MonitorBrightness = monitors.ToDictionary(
                m => m.MonitorId,
                m => m.CurrentBrightness)
        };

        _logger.Information(
            "Creating profile '{Name}' from {Count} monitor(s)",
            name, monitors.Count);

        await SaveProfileAsync(profile);
        return profile;
    }

    public async Task SetActiveProfileAsync(string? id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();

            if (id is not null && store.Profiles.All(p => p.Id != id))
            {
                _logger.Warning("Cannot set active profile to non-existent id {Id}", id);
                return;
            }

            store.ActiveProfileId = id;
            await PersistStoreAsync(store);

            _logger.Information("Active profile set to {Id}", id ?? "(none)");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string?> GetActiveProfileIdAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var store = await LoadStoreAsync();
            return store.ActiveProfileId;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<ProfileStore> LoadStoreAsync()
    {
        if (!File.Exists(_profilesPath))
        {
            _logger.Debug("Profile store file not found, returning empty store");
            return new ProfileStore();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_profilesPath);
            var store = JsonSerializer.Deserialize<ProfileStore>(json, _jsonOptions);
            return store ?? new ProfileStore();
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to deserialize profile store, returning empty store");
            return new ProfileStore();
        }
        catch (IOException ex)
        {
            _logger.Error(ex, "Failed to read profile store file");
            return new ProfileStore();
        }
    }

    private async Task PersistStoreAsync(ProfileStore store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, _jsonOptions);
            await File.WriteAllTextAsync(_profilesPath, json);
            _logger.Debug("Profile store persisted with {Count} profile(s)", store.Profiles.Count);
        }
        catch (IOException ex)
        {
            _logger.Error(ex, "Failed to write profile store file");
            throw;
        }
    }

    private void OnProfilesChanged(List<BrightnessProfile> profiles)
    {
        ProfilesChanged?.Invoke(this, profiles);
    }
}
