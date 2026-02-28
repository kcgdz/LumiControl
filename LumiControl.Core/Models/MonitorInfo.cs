namespace LumiControl.Core.Models;

public class MonitorInfo
{
    public string MonitorId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = "Unknown Monitor";
    public int CurrentBrightness { get; set; }
    public int MinBrightness { get; set; }
    public int MaxBrightness { get; set; } = 100;
    public bool IsInternal { get; set; }
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Unknown;
    public IntPtr PhysicalMonitorHandle { get; set; }
    public string DevicePath { get; set; } = string.Empty;
    public bool SupportsDDCCI { get; set; }
}

public enum ConnectionType
{
    Unknown,
    Internal,
    HDMI,
    DisplayPort,
    VGA,
    DVI,
    USB_C,
    Thunderbolt
}
