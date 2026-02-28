using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiControl.Core.Models;
using LumiControl.Core.Services;

namespace LumiControl.Models;

public partial class MonitorViewModel : ObservableObject
{
    private readonly IMonitorService _monitorService;
    private readonly MonitorInfo _monitorInfo;

    public MonitorViewModel(MonitorInfo monitorInfo, IMonitorService monitorService)
    {
        _monitorInfo = monitorInfo;
        _monitorService = monitorService;
        _brightness = monitorInfo.CurrentBrightness;
    }

    public string MonitorId => _monitorInfo.MonitorId;
    public string FriendlyName => _monitorInfo.FriendlyName;
    public bool IsInternal => _monitorInfo.IsInternal;
    public ConnectionType ConnectionType => _monitorInfo.ConnectionType;
    public bool SupportsDDCCI => _monitorInfo.SupportsDDCCI;
    public int MinBrightness => _monitorInfo.MinBrightness;
    public int MaxBrightness => _monitorInfo.MaxBrightness;

    [ObservableProperty]
    private int _brightness;

    [ObservableProperty]
    private bool _isUpdating;

    partial void OnBrightnessChanged(int value)
    {
        _ = SetBrightnessInternalAsync(value);
    }

    private async Task SetBrightnessInternalAsync(int value)
    {
        if (IsUpdating) return;
        IsUpdating = true;
        try
        {
            var clamped = Math.Clamp(value, MinBrightness, MaxBrightness);
            await _monitorService.SetBrightnessAsync(MonitorId, clamped);
            _monitorInfo.CurrentBrightness = clamped;
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task SetQuickBrightness(string percent)
    {
        if (int.TryParse(percent, out var val))
        {
            Brightness = val;
        }
    }

    public void UpdateFromInfo(MonitorInfo info)
    {
        if (!IsUpdating)
        {
            _brightness = info.CurrentBrightness;
            OnPropertyChanged(nameof(Brightness));
        }
    }

    public string MonitorIcon => IsInternal ? "\uE7F4" : "\uE7F8";

    public string MonitorSubtitle => IsInternal
        ? "Built-in Display"
        : $"{ConnectionBadge} • {(SupportsDDCCI ? "DDC/CI" : "No DDC/CI")}";
    public string ConnectionBadge => ConnectionType switch
    {
        ConnectionType.HDMI => "HDMI",
        ConnectionType.DisplayPort => "DP",
        ConnectionType.VGA => "VGA",
        ConnectionType.DVI => "DVI",
        ConnectionType.USB_C => "USB-C",
        ConnectionType.Thunderbolt => "TB",
        ConnectionType.Internal => "Built-in",
        _ => "External"
    };
}
