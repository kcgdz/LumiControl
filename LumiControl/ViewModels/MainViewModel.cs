using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiControl.Core.Models;
using LumiControl.Core.Services;
using LumiControl.Models;
using LumiControl.Services;
using Serilog;

namespace LumiControl.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMonitorService _monitorService;
    private readonly IProfileService _profileService;
    private readonly IScheduleService _scheduleService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    public ObservableCollection<MonitorViewModel> Monitors { get; } = new();
    public ObservableCollection<BrightnessProfile> Profiles { get; } = new();

    [ObservableProperty]
    private int _masterBrightness = 50;

    [ObservableProperty]
    private bool _isAutoScheduleEnabled;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _statusText = "Detecting monitors...";

    [ObservableProperty]
    private BrightnessProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isSettingsOpen;

    private bool _suppressMasterUpdate;

    public MainViewModel(
        IMonitorService monitorService,
        IProfileService profileService,
        IScheduleService scheduleService,
        ISettingsService settingsService,
        ILogger logger)
    {
        _monitorService = monitorService;
        _profileService = profileService;
        _scheduleService = scheduleService;
        _settingsService = settingsService;
        _logger = logger.ForContext<MainViewModel>();

        _monitorService.MonitorsChanged += OnMonitorsChanged;
        _profileService.ProfilesChanged += OnProfilesChanged;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        StatusText = "Detecting monitors...";

        try
        {
            var monitors = await _monitorService.DetectMonitorsAsync();
            UpdateMonitorList(monitors);

            var profiles = await _profileService.GetProfilesAsync();
            UpdateProfileList(profiles);

            await _scheduleService.LoadAsync();
            IsAutoScheduleEnabled = _settingsService.Settings.Schedule.Rules.Count > 0;

            if (IsAutoScheduleEnabled)
                await _scheduleService.StartAsync();

            StatusText = $"{Monitors.Count} monitor(s) detected";
            _logger.Information("Initialized with {Count} monitors", Monitors.Count);
        }
        catch (Exception ex)
        {
            StatusText = "Error detecting monitors";
            _logger.Error(ex, "Failed to initialize");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnMasterBrightnessChanged(int value)
    {
        if (_suppressMasterUpdate) return;
        _ = SetAllBrightnessAsync(value);
    }

    private async Task SetAllBrightnessAsync(int brightness)
    {
        foreach (var monitor in Monitors)
        {
            monitor.Brightness = brightness;
        }
    }

    private void UpdateMonitorList(List<MonitorInfo> monitors)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Monitors.Clear();
            foreach (var info in monitors)
            {
                Monitors.Add(new MonitorViewModel(info, _monitorService));
            }
            RecalculateMasterBrightness();
        });
    }

    private void RecalculateMasterBrightness()
    {
        if (Monitors.Count == 0) return;
        _suppressMasterUpdate = true;
        MasterBrightness = (int)Monitors.Average(m => m.Brightness);
        _suppressMasterUpdate = false;
    }

    private void UpdateProfileList(List<BrightnessProfile> profiles)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Profiles.Clear();
            foreach (var p in profiles)
                Profiles.Add(p);
        });
    }

    private void OnMonitorsChanged(object? sender, List<MonitorInfo> monitors)
    {
        UpdateMonitorList(monitors);
        StatusText = $"{monitors.Count} monitor(s) detected";
    }

    private void OnProfilesChanged(object? sender, List<BrightnessProfile> profiles)
    {
        UpdateProfileList(profiles);
    }

    [RelayCommand]
    private async Task RefreshMonitors()
    {
        IsLoading = true;
        StatusText = "Refreshing...";
        try
        {
            var monitors = await _monitorService.DetectMonitorsAsync();
            UpdateMonitorList(monitors);
            StatusText = $"{Monitors.Count} monitor(s) detected";
        }
        catch (Exception ex)
        {
            StatusText = "Refresh failed";
            _logger.Error(ex, "Refresh failed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var monitorInfos = Monitors.Select(m => new MonitorInfo
        {
            MonitorId = m.MonitorId,
            CurrentBrightness = m.Brightness
        }).ToList();

        await _profileService.CreateFromCurrentAsync(name, monitorInfos);
        _logger.Information("Profile saved: {Name}", name);
    }

    [RelayCommand]
    private async Task ApplyProfile(BrightnessProfile? profile)
    {
        if (profile == null) return;

        foreach (var monitor in Monitors)
        {
            if (profile.MonitorBrightness.TryGetValue(monitor.MonitorId, out var brightness))
            {
                monitor.Brightness = brightness;
            }
        }

        await _profileService.SetActiveProfileAsync(profile.Id);
        SelectedProfile = profile;
        _logger.Information("Profile applied: {Name}", profile.Name);
    }

    [RelayCommand]
    private async Task DeleteProfile(BrightnessProfile? profile)
    {
        if (profile == null) return;
        await _profileService.DeleteProfileAsync(profile.Id);
        if (SelectedProfile?.Id == profile.Id)
            SelectedProfile = null;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
    }

    partial void OnIsAutoScheduleEnabledChanged(bool value)
    {
        if (value)
            _ = _scheduleService.StartAsync();
        else
            _ = _scheduleService.StopAsync();
    }

    public void AdjustBrightness(int delta, string? monitorId = null)
    {
        if (monitorId == null)
        {
            foreach (var m in Monitors)
                m.Brightness = Math.Clamp(m.Brightness + delta, m.MinBrightness, m.MaxBrightness);
        }
        else
        {
            var monitor = Monitors.FirstOrDefault(m => m.MonitorId == monitorId);
            if (monitor != null)
                monitor.Brightness = Math.Clamp(monitor.Brightness + delta, monitor.MinBrightness, monitor.MaxBrightness);
        }
    }
}
