using LumiControl.Core.Models;
using Serilog;

namespace LumiControl.Core.Services;

/// <summary>
/// A mock implementation of <see cref="IMonitorService"/> that simulates three monitors
/// without requiring real hardware. Intended for testing and development purposes.
/// </summary>
public class MockMonitorService : IMonitorService
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, int> _brightnessValues = new();
    private readonly List<MonitorInfo> _monitors;
    private bool _disposed;

    public event EventHandler<List<MonitorInfo>>? MonitorsChanged;

    public MockMonitorService(ILogger logger)
    {
        _logger = logger.ForContext<MockMonitorService>();

        _monitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                MonitorId = "MOCK-INTERNAL-001",
                FriendlyName = "Internal Display",
                CurrentBrightness = 70,
                MinBrightness = 0,
                MaxBrightness = 100,
                IsInternal = true,
                ConnectionType = ConnectionType.Internal,
                PhysicalMonitorHandle = IntPtr.Zero,
                DevicePath = @"\\.\DISPLAY1\Mock\Internal",
                SupportsDDCCI = false
            },
            new MonitorInfo
            {
                MonitorId = "MOCK-DELL-002",
                FriendlyName = "DELL U2722D",
                CurrentBrightness = 50,
                MinBrightness = 0,
                MaxBrightness = 100,
                IsInternal = false,
                ConnectionType = ConnectionType.DisplayPort,
                PhysicalMonitorHandle = IntPtr.Zero,
                DevicePath = @"\\.\DISPLAY2\Mock\DisplayPort",
                SupportsDDCCI = true
            },
            new MonitorInfo
            {
                MonitorId = "MOCK-LG-003",
                FriendlyName = "LG 27UK850",
                CurrentBrightness = 80,
                MinBrightness = 0,
                MaxBrightness = 100,
                IsInternal = false,
                ConnectionType = ConnectionType.HDMI,
                PhysicalMonitorHandle = IntPtr.Zero,
                DevicePath = @"\\.\DISPLAY3\Mock\HDMI",
                SupportsDDCCI = true
            }
        };

        foreach (var monitor in _monitors)
        {
            _brightnessValues[monitor.MonitorId] = monitor.CurrentBrightness;
        }

        _logger.Information("MockMonitorService initialized with {Count} simulated monitors", _monitors.Count);
    }

    public async Task<List<MonitorInfo>> DetectMonitorsAsync()
    {
        ThrowIfDisposed();

        _logger.Debug("Detecting simulated monitors...");

        // Simulate hardware detection delay
        await Task.Delay(Random.Shared.Next(50, 101));

        // Update current brightness values from the backing store
        foreach (var monitor in _monitors)
        {
            if (_brightnessValues.TryGetValue(monitor.MonitorId, out var brightness))
            {
                monitor.CurrentBrightness = brightness;
            }
        }

        _logger.Information("Detected {Count} simulated monitors", _monitors.Count);

        return new List<MonitorInfo>(_monitors);
    }

    public async Task<bool> SetBrightnessAsync(string monitorId, int brightness)
    {
        ThrowIfDisposed();

        _logger.Debug("Setting brightness for monitor {MonitorId} to {Brightness}", monitorId, brightness);

        // Simulate hardware communication delay
        await Task.Delay(Random.Shared.Next(50, 101));

        if (!_brightnessValues.ContainsKey(monitorId))
        {
            _logger.Warning("Monitor {MonitorId} not found", monitorId);
            return false;
        }

        var monitor = _monitors.FirstOrDefault(m => m.MonitorId == monitorId);
        if (monitor == null)
        {
            _logger.Warning("Monitor {MonitorId} not found in monitor list", monitorId);
            return false;
        }

        int clampedBrightness = Math.Clamp(brightness, monitor.MinBrightness, monitor.MaxBrightness);
        if (clampedBrightness != brightness)
        {
            _logger.Warning(
                "Brightness value {Requested} clamped to {Clamped} for monitor {MonitorId}",
                brightness, clampedBrightness, monitorId);
        }

        _brightnessValues[monitorId] = clampedBrightness;
        monitor.CurrentBrightness = clampedBrightness;

        _logger.Information(
            "Brightness set to {Brightness} for monitor {MonitorId} ({Name})",
            clampedBrightness, monitorId, monitor.FriendlyName);

        return true;
    }

    public async Task<int> GetBrightnessAsync(string monitorId)
    {
        ThrowIfDisposed();

        _logger.Debug("Getting brightness for monitor {MonitorId}", monitorId);

        // Simulate hardware communication delay
        await Task.Delay(Random.Shared.Next(50, 101));

        if (_brightnessValues.TryGetValue(monitorId, out var brightness))
        {
            _logger.Debug("Current brightness for monitor {MonitorId}: {Brightness}", monitorId, brightness);
            return brightness;
        }

        _logger.Warning("Monitor {MonitorId} not found, returning -1", monitorId);
        return -1;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MockMonitorService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.Information("Disposing MockMonitorService");
        _brightnessValues.Clear();
        _monitors.Clear();
        _disposed = true;
    }
}
