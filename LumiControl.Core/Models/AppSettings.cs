namespace LumiControl.Core.Models;

public class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool UseMockMonitors { get; set; }
    public HotkeySettings Hotkeys { get; set; } = new();
    public ScheduleStore Schedule { get; set; } = new();
}

public class HotkeySettings
{
    public HotkeyBinding? BrightnessUp { get; set; }
    public HotkeyBinding? BrightnessDown { get; set; }
    public Dictionary<string, HotkeyBinding> PerMonitorUp { get; set; } = new();
    public Dictionary<string, HotkeyBinding> PerMonitorDown { get; set; } = new();
    public int BrightnessStep { get; set; } = 10;
}

public class HotkeyBinding
{
    public int KeyCode { get; set; }
    public int Modifiers { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
