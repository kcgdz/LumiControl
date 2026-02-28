namespace LumiControl.Core.Models;

public class ScheduleRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public TimeOnly StartTime { get; set; }
    public int Brightness { get; set; }
    public int TransitionMinutes { get; set; } = 15;
    public string? MonitorId { get; set; } // null = all monitors
    public List<DayOfWeek> Days { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };
}

public class ScheduleStore
{
    public List<ScheduleRule> Rules { get; set; } = new();
    public bool UseSunriseSunset { get; set; }
    public int SunriseBrightness { get; set; } = 80;
    public int SunsetBrightness { get; set; } = 20;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
