using System.Management;
using System.Runtime.InteropServices;
using LumiControl.Core.Models;
using LumiControl.Core.Native;
using Serilog;

namespace LumiControl.Core.Services;

public interface IMonitorService : IDisposable
{
    Task<List<MonitorInfo>> DetectMonitorsAsync();
    Task<bool> SetBrightnessAsync(string monitorId, int brightness);
    Task<int> GetBrightnessAsync(string monitorId);
    event EventHandler<List<MonitorInfo>>? MonitorsChanged;
}

public sealed class MonitorService : IMonitorService
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, PHYSICAL_MONITOR[]> _physicalMonitorHandles = new();
    private readonly object _lock = new();
    private List<MonitorInfo> _cachedMonitors = [];
    private bool _disposed;

    public event EventHandler<List<MonitorInfo>>? MonitorsChanged;

    public MonitorService(ILogger logger)
    {
        _logger = logger.ForContext<MonitorService>();
    }

    public async Task<List<MonitorInfo>> DetectMonitorsAsync()
    {
        ThrowIfDisposed();
        _logger.Information("Starting monitor detection");

        return await Task.Run(() =>
        {
            lock (_lock)
            {
                CleanupHandles();

                var monitors = new List<MonitorInfo>();

                try
                {
                    DetectWmiMonitors(monitors);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "WMI monitor detection failed — no internal displays will be reported");
                }

                try
                {
                    DetectPhysicalMonitors(monitors);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Physical monitor detection via DDC/CI failed");
                }

                var countChanged = _cachedMonitors.Count != monitors.Count
                    || !_cachedMonitors.Select(m => m.MonitorId).SequenceEqual(monitors.Select(m => m.MonitorId));

                _cachedMonitors = monitors;
                _logger.Information("Detected {Count} monitor(s)", monitors.Count);

                // Only fire event when monitor set actually changed (plugged/unplugged)
                if (countChanged)
                    MonitorsChanged?.Invoke(this, monitors);

                return monitors;
            }
        });
    }

    public async Task<bool> SetBrightnessAsync(string monitorId, int brightness)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

        return await Task.Run(() =>
        {
            lock (_lock)
            {
                var monitor = _cachedMonitors.FirstOrDefault(m => m.MonitorId == monitorId);
                if (monitor is null)
                {
                    _logger.Warning("Monitor {MonitorId} not found in cached list", monitorId);
                    return false;
                }

                brightness = Math.Clamp(brightness, monitor.MinBrightness, monitor.MaxBrightness);

                if (monitor.IsInternal)
                {
                    return SetWmiBrightness(brightness);
                }

                return SetPhysicalMonitorBrightness(monitor, (uint)brightness);
            }
        });
    }

    public async Task<int> GetBrightnessAsync(string monitorId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

        return await Task.Run(() =>
        {
            lock (_lock)
            {
                var monitor = _cachedMonitors.FirstOrDefault(m => m.MonitorId == monitorId);
                if (monitor is null)
                {
                    _logger.Warning("Monitor {MonitorId} not found in cached list", monitorId);
                    return -1;
                }

                if (monitor.IsInternal)
                {
                    return GetWmiBrightness();
                }

                return GetPhysicalMonitorBrightness(monitor);
            }
        });
    }

    // ----------------------------------------------------------------
    //  WMI detection & control (internal / laptop displays)
    // ----------------------------------------------------------------

    private void DetectWmiMonitors(List<MonitorInfo> monitors)
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\WMI",
            "SELECT * FROM WmiMonitorBrightness");

        using var results = searcher.Get();

        foreach (ManagementObject obj in results)
        {
            var instanceName = obj["InstanceName"]?.ToString() ?? "WMI_INTERNAL";

            int currentBrightness = Convert.ToInt32(obj["CurrentBrightness"]);
            var levels = obj["Level"] as byte[] ?? [];

            int minBrightness = levels.Length > 0 ? levels.Min() : 0;
            int maxBrightness = levels.Length > 0 ? levels.Max() : 100;

            var monitorId = $"WMI_{SanitizeId(instanceName)}";

            // Avoid duplicates if this internal display was already picked up
            if (monitors.Any(m => m.MonitorId == monitorId))
                continue;

            var info = new MonitorInfo
            {
                MonitorId = monitorId,
                FriendlyName = GetWmiMonitorFriendlyName(instanceName),
                CurrentBrightness = currentBrightness,
                MinBrightness = minBrightness,
                MaxBrightness = maxBrightness,
                IsInternal = true,
                ConnectionType = ConnectionType.Internal,
                DevicePath = instanceName,
                SupportsDDCCI = false
            };

            monitors.Add(info);
            _logger.Debug(
                "Detected WMI monitor: {Name} (brightness {Brightness}%)",
                info.FriendlyName, currentBrightness);
        }
    }

    private static string GetWmiMonitorFriendlyName(string instanceName)
    {
        try
        {
            // Trim trailing _0 etc. that WMI appends for matching against
            // WmiMonitorID which uses the base instance path.
            var baseName = instanceName.Contains('_')
                ? instanceName[..instanceName.LastIndexOf('_')]
                : instanceName;

            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                $"SELECT UserFriendlyName FROM WmiMonitorID WHERE InstanceName LIKE '{EscapeWql(baseName)}%'");

            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                if (obj["UserFriendlyName"] is ushort[] charCodes && charCodes.Length > 0)
                {
                    var name = new string(charCodes
                        .TakeWhile(c => c != 0)
                        .Select(c => (char)c)
                        .ToArray());

                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
        }
        catch
        {
            // Swallow — friendly name is best-effort
        }

        return "Built-in Display";
    }

    private static int GetWmiBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness");

            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                return Convert.ToInt32(obj["CurrentBrightness"]);
            }
        }
        catch
        {
            // Fall through
        }

        return -1;
    }

    private bool SetWmiBrightness(int brightness)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT * FROM WmiMonitorBrightnessMethods");

            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                obj.InvokeMethod("WmiSetBrightness", [
                    (uint)1,       // timeout — 1 second
                    (byte)brightness
                ]);

                _logger.Debug("WMI brightness set to {Brightness}%", brightness);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set WMI brightness to {Brightness}", brightness);
        }

        return false;
    }

    // ----------------------------------------------------------------
    //  Physical Monitor API (external DDC/CI displays)
    // ----------------------------------------------------------------

    private void DetectPhysicalMonitors(List<MonitorInfo> monitors)
    {
        var hMonitors = new List<IntPtr>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
            {
                hMonitors.Add(hMonitor);
                return true;
            },
            IntPtr.Zero);

        _logger.Debug("EnumDisplayMonitors returned {Count} logical monitor(s)", hMonitors.Count);

        foreach (var hMonitor in hMonitors)
        {
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
            {
                _logger.Debug("No physical monitors for HMONITOR {Handle}", hMonitor);
                continue;
            }

            var physicalMonitors = new PHYSICAL_MONITOR[count];

            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
            {
                _logger.Warning(
                    "GetPhysicalMonitorsFromHMONITOR failed for {Handle} (error {Error})",
                    hMonitor, Marshal.GetLastWin32Error());
                continue;
            }

            // Resolve device name from MONITORINFOEX
            var monitorInfoEx = MONITORINFOEX.Create();
            string deviceName = string.Empty;
            if (NativeMethods.GetMonitorInfo(hMonitor, ref monitorInfoEx))
            {
                deviceName = monitorInfoEx.szDevice;
            }

            for (int i = 0; i < physicalMonitors.Length; i++)
            {
                var pm = physicalMonitors[i];
                var handle = pm.hPhysicalMonitor;

                // Skip monitors that were already detected as WMI (internal)
                bool isInternal = IsInternalDisplay(deviceName);
                if (isInternal && monitors.Any(m => m.IsInternal))
                {
                    _logger.Debug(
                        "Skipping physical monitor for internal display {Device} — already tracked via WMI",
                        deviceName);
                    continue;
                }

                bool supportsDdc = TryGetBrightness(
                    handle,
                    out uint minBrightness,
                    out uint currentBrightness,
                    out uint maxBrightness);

                var monitorId = $"PHY_{SanitizeId(deviceName)}_{i}";

                // Always try EDID/WMI first — szPhysicalMonitorDescription is usually just "Generic PnP Monitor"
                var friendlyName = ResolveDisplayDeviceName(deviceName);
                if (friendlyName == "External Monitor"
                    && !string.IsNullOrWhiteSpace(pm.szPhysicalMonitorDescription)
                    && !pm.szPhysicalMonitorDescription.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    friendlyName = pm.szPhysicalMonitorDescription;
                }

                var connectionType = DetectConnectionType(deviceName, friendlyName);

                var info = new MonitorInfo
                {
                    MonitorId = monitorId,
                    FriendlyName = friendlyName,
                    CurrentBrightness = supportsDdc ? (int)currentBrightness : 0,
                    MinBrightness = supportsDdc ? (int)minBrightness : 0,
                    MaxBrightness = supportsDdc ? (int)maxBrightness : 100,
                    IsInternal = isInternal,
                    ConnectionType = connectionType,
                    PhysicalMonitorHandle = handle,
                    DevicePath = deviceName,
                    SupportsDDCCI = supportsDdc
                };

                monitors.Add(info);
                _physicalMonitorHandles[monitorId] = physicalMonitors;

                _logger.Debug(
                    "Detected physical monitor: {Name} (DDC/CI={Ddc}, brightness {Brightness}/{Max})",
                    info.FriendlyName, supportsDdc, info.CurrentBrightness, info.MaxBrightness);
            }
        }
    }

    private bool TryGetBrightness(
        IntPtr handle,
        out uint minBrightness,
        out uint currentBrightness,
        out uint maxBrightness)
    {
        minBrightness = 0;
        currentBrightness = 0;
        maxBrightness = 100;

        try
        {
            // First check if the monitor advertises brightness capability
            if (NativeMethods.GetMonitorCapabilities(handle, out uint caps, out _))
            {
                if ((caps & NativeMethods.MC_CAPS_BRIGHTNESS) == 0)
                {
                    _logger.Debug("Monitor handle {Handle} does not advertise brightness capability", handle);
                    return false;
                }
            }

            if (NativeMethods.GetMonitorBrightness(handle, out minBrightness, out currentBrightness, out maxBrightness))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            _logger.Debug(
                "GetMonitorBrightness failed for handle {Handle} (Win32 error {Error})",
                handle, error);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Exception while querying brightness for handle {Handle}", handle);
        }

        return false;
    }

    private bool SetPhysicalMonitorBrightness(MonitorInfo monitor, uint brightness)
    {
        if (!monitor.SupportsDDCCI)
        {
            _logger.Warning(
                "Monitor {MonitorId} does not support DDC/CI — cannot set brightness",
                monitor.MonitorId);
            return false;
        }

        try
        {
            if (NativeMethods.SetMonitorBrightness(monitor.PhysicalMonitorHandle, brightness))
            {
                _logger.Debug(
                    "Set brightness to {Brightness} on monitor {MonitorId}",
                    brightness, monitor.MonitorId);
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            _logger.Error(
                "SetMonitorBrightness failed for {MonitorId} (Win32 error {Error})",
                monitor.MonitorId, error);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception setting brightness for monitor {MonitorId}", monitor.MonitorId);
        }

        return false;
    }

    private int GetPhysicalMonitorBrightness(MonitorInfo monitor)
    {
        if (!monitor.SupportsDDCCI)
        {
            _logger.Warning(
                "Monitor {MonitorId} does not support DDC/CI — cannot read brightness",
                monitor.MonitorId);
            return -1;
        }

        if (NativeMethods.GetMonitorBrightness(
                monitor.PhysicalMonitorHandle,
                out _,
                out uint current,
                out _))
        {
            return (int)current;
        }

        _logger.Warning("Failed to read brightness from monitor {MonitorId}", monitor.MonitorId);
        return -1;
    }

    // ----------------------------------------------------------------
    //  Display metadata helpers
    // ----------------------------------------------------------------

    private static bool IsInternalDisplay(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return false;

        // Enumerate child display devices. Internal panels typically have
        // "DISPLAY" in their DeviceID containing "LVDS", "eDP", or "DSI"
        // prefixes, or the adapter description hints at integrated graphics.
        var displayDevice = DISPLAY_DEVICE.Create();

        if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref displayDevice, 0x00000001))
        {
            var id = displayDevice.DeviceID?.ToUpperInvariant() ?? string.Empty;
            return id.Contains("LVDS")
                || id.Contains("EDP")
                || id.Contains("DSI")
                || id.Contains("INTERNAL");
        }

        return false;
    }

    private static string ResolveDisplayDeviceName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return "External Monitor";

        // Try getting the monitor's Device ID to look up EDID in registry
        var displayDevice = DISPLAY_DEVICE.Create();
        if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref displayDevice, 0x00000001))
        {
            // Try reading the friendly name from EDID in registry
            var edidName = GetFriendlyNameFromEdid(displayDevice.DeviceID);
            if (!string.IsNullOrWhiteSpace(edidName))
                return edidName;
        }

        // Try WMI WmiMonitorID for all monitors (works even on desktop)
        var wmiName = GetFriendlyNameFromWmiMonitorId(deviceName);
        if (!string.IsNullOrWhiteSpace(wmiName))
            return wmiName;

        // Final fallback to EnumDisplayDevices DeviceString
        if (!string.IsNullOrWhiteSpace(displayDevice.DeviceString)
            && !displayDevice.DeviceString.Contains("Generic", StringComparison.OrdinalIgnoreCase))
        {
            return displayDevice.DeviceString.Trim();
        }

        return "External Monitor";
    }

    private static string? GetFriendlyNameFromEdid(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        try
        {
            // DeviceID format: MONITOR\{manufacturer}{product}\{instance}
            // Registry path: HKLM\SYSTEM\CurrentControlSet\Enum\{DeviceID}\Device Parameters\EDID
            var cleanId = deviceId.TrimStart('\\', '?');
            // Convert \\?\DISPLAY#... to SYSTEM\CurrentControlSet\Enum\DISPLAY\...
            cleanId = cleanId.Replace('#', '\\');
            // Remove trailing \{GUID} if present
            var guidIdx = cleanId.IndexOf('{');
            if (guidIdx > 0)
                cleanId = cleanId[..guidIdx].TrimEnd('\\');

            var regPath = $@"SYSTEM\CurrentControlSet\Enum\{cleanId}\Device Parameters";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
            if (key?.GetValue("EDID") is byte[] edid && edid.Length >= 128)
            {
                return ParseEdidFriendlyName(edid);
            }
        }
        catch
        {
            // EDID read is best-effort
        }

        return null;
    }

    private static string? ParseEdidFriendlyName(byte[] edid)
    {
        // EDID descriptor blocks start at offset 54, each is 18 bytes
        // Descriptor tag 0xFC = Monitor name
        for (int i = 54; i + 18 <= edid.Length && i < 54 + 4 * 18; i += 18)
        {
            // Check for monitor name descriptor: bytes 0-2 = 0x00, byte 3 = 0xFC
            if (edid[i] == 0 && edid[i + 1] == 0 && edid[i + 2] == 0 && edid[i + 3] == 0xFC)
            {
                // Name starts at offset 5, up to 13 chars, terminated by 0x0A
                var nameBytes = new byte[13];
                Array.Copy(edid, i + 5, nameBytes, 0, 13);
                var name = System.Text.Encoding.ASCII.GetString(nameBytes)
                    .TrimEnd('\n', '\r', ' ', '\0');
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        return null;
    }

    private static string? GetFriendlyNameFromWmiMonitorId(string deviceName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT UserFriendlyName, InstanceName FROM WmiMonitorID");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                if (obj["UserFriendlyName"] is ushort[] charCodes && charCodes.Length > 0)
                {
                    var name = new string(charCodes
                        .TakeWhile(c => c != 0)
                        .Select(c => (char)c)
                        .ToArray()).Trim();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        // Try to match instance name to device name
                        var instance = obj["InstanceName"]?.ToString() ?? "";
                        // If we can't match specifically, return first valid name
                        if (deviceName.Contains("DISPLAY1") && instance.Contains("DISPLAY1", StringComparison.OrdinalIgnoreCase))
                            return name;
                        if (deviceName.Contains("DISPLAY2") && instance.Contains("DISPLAY2", StringComparison.OrdinalIgnoreCase))
                            return name;
                        if (deviceName.Contains("DISPLAY3") && instance.Contains("DISPLAY3", StringComparison.OrdinalIgnoreCase))
                            return name;
                    }
                }
            }

            // Second pass: try matching by index
            int displayIndex = -1;
            for (int d = 9; d >= 1; d--)
            {
                if (deviceName.Contains($"DISPLAY{d}", StringComparison.OrdinalIgnoreCase))
                {
                    displayIndex = d;
                    break;
                }
            }

            if (displayIndex > 0)
            {
                int idx = 0;
                foreach (ManagementObject obj in results)
                {
                    idx++;
                    if (idx == displayIndex && obj["UserFriendlyName"] is ushort[] codes && codes.Length > 0)
                    {
                        var name = new string(codes
                            .TakeWhile(c => c != 0)
                            .Select(c => (char)c)
                            .ToArray()).Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }
        }
        catch
        {
            // WmiMonitorID is best-effort
        }

        return null;
    }

    private static ConnectionType DetectConnectionType(string deviceName, string description)
    {
        var combined = $"{deviceName} {description}".ToUpperInvariant();

        if (combined.Contains("LVDS") || combined.Contains("EDP") || combined.Contains("DSI"))
            return ConnectionType.Internal;
        if (combined.Contains("DISPLAYPORT") || combined.Contains("DP"))
            return ConnectionType.DisplayPort;
        if (combined.Contains("HDMI"))
            return ConnectionType.HDMI;
        if (combined.Contains("DVI"))
            return ConnectionType.DVI;
        if (combined.Contains("VGA") || combined.Contains("DSUB") || combined.Contains("D-SUB"))
            return ConnectionType.VGA;
        if (combined.Contains("USB-C") || combined.Contains("USB_C") || combined.Contains("USBC"))
            return ConnectionType.USB_C;
        if (combined.Contains("THUNDERBOLT") || combined.Contains("TBT"))
            return ConnectionType.Thunderbolt;

        return ConnectionType.Unknown;
    }

    // ----------------------------------------------------------------
    //  Utilities
    // ----------------------------------------------------------------

    private static string SanitizeId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "UNKNOWN";

        // Replace path-unfriendly characters with underscores, then trim
        return string.Concat(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_'))
            .Trim('_');
    }

    private static string EscapeWql(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'");
    }

    // ----------------------------------------------------------------
    //  Handle cleanup / IDisposable
    // ----------------------------------------------------------------

    private void CleanupHandles()
    {
        foreach (var (monitorId, handles) in _physicalMonitorHandles)
        {
            try
            {
                NativeMethods.DestroyPhysicalMonitors((uint)handles.Length, handles);
                _logger.Debug("Destroyed physical monitor handles for {MonitorId}", monitorId);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to destroy physical monitor handles for {MonitorId}", monitorId);
            }
        }

        _physicalMonitorHandles.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_lock)
        {
            CleanupHandles();
            _cachedMonitors.Clear();
        }

        _logger.Debug("MonitorService disposed");
    }
}
