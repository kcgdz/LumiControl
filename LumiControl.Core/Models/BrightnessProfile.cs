namespace LumiControl.Core.Models;

public class BrightnessProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();
}

public class ProfileStore
{
    public List<BrightnessProfile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
}
