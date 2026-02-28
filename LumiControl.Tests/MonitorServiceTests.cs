using LumiControl.Core.Services;
using Serilog.Core;
using Xunit;

namespace LumiControl.Tests;

public class MonitorServiceTests : IDisposable
{
    private readonly MockMonitorService _service;

    public MonitorServiceTests()
    {
        _service = new MockMonitorService(Logger.None);
    }

    [Fact]
    public async Task DetectMonitorsAsync_ReturnsThreeMonitors()
    {
        var monitors = await _service.DetectMonitorsAsync();

        Assert.Equal(3, monitors.Count);
    }

    [Theory]
    [InlineData("Internal Display")]
    [InlineData("DELL U2722D")]
    [InlineData("LG 27UK850")]
    public async Task DetectMonitorsAsync_ContainsExpectedMonitorByName(string expectedName)
    {
        var monitors = await _service.DetectMonitorsAsync();

        Assert.Contains(monitors, m => m.FriendlyName == expectedName);
    }

    [Fact]
    public async Task SetBrightnessAsync_ChangeBrightnessSuccessfully()
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[0].MonitorId;

        bool result = await _service.SetBrightnessAsync(monitorId, 42);

        Assert.True(result);
    }

    [Fact]
    public async Task GetBrightnessAsync_ReturnsCorrectValueAfterSet()
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[0].MonitorId;

        await _service.SetBrightnessAsync(monitorId, 55);
        int brightness = await _service.GetBrightnessAsync(monitorId);

        Assert.Equal(55, brightness);
    }

    [Theory]
    [InlineData(150, 100)]
    [InlineData(-20, 0)]
    [InlineData(200, 100)]
    [InlineData(-1, 0)]
    public async Task SetBrightnessAsync_ClampsBrightnessToValidRange(int requested, int expectedClamped)
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[0].MonitorId;

        await _service.SetBrightnessAsync(monitorId, requested);
        int actual = await _service.GetBrightnessAsync(monitorId);

        Assert.Equal(expectedClamped, actual);
    }

    [Fact]
    public async Task SetBrightnessAsync_InvalidMonitorId_ReturnsFalse()
    {
        bool result = await _service.SetBrightnessAsync("NON-EXISTENT-ID", 50);

        Assert.False(result);
    }

    [Fact]
    public async Task GetBrightnessAsync_InvalidMonitorId_ReturnsNegativeOne()
    {
        int brightness = await _service.GetBrightnessAsync("NON-EXISTENT-ID");

        Assert.Equal(-1, brightness);
    }

    [Fact]
    public async Task SetBrightnessAsync_BoundaryValue_Zero()
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[0].MonitorId;

        bool result = await _service.SetBrightnessAsync(monitorId, 0);
        int brightness = await _service.GetBrightnessAsync(monitorId);

        Assert.True(result);
        Assert.Equal(0, brightness);
    }

    [Fact]
    public async Task SetBrightnessAsync_BoundaryValue_OneHundred()
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[0].MonitorId;

        bool result = await _service.SetBrightnessAsync(monitorId, 100);
        int brightness = await _service.GetBrightnessAsync(monitorId);

        Assert.True(result);
        Assert.Equal(100, brightness);
    }

    [Fact]
    public async Task DetectMonitorsAsync_ReflectsUpdatedBrightness()
    {
        var monitors = await _service.DetectMonitorsAsync();
        var monitorId = monitors[1].MonitorId;

        await _service.SetBrightnessAsync(monitorId, 33);

        var refreshedMonitors = await _service.DetectMonitorsAsync();
        var monitor = refreshedMonitors.First(m => m.MonitorId == monitorId);

        Assert.Equal(33, monitor.CurrentBrightness);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
