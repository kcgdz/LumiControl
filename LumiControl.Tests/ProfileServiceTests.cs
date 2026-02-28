using LumiControl.Core.Models;
using LumiControl.Core.Services;
using Serilog.Core;
using Xunit;

namespace LumiControl.Tests;

public class ProfileServiceTests : IDisposable
{
    private readonly ProfileService _service;
    private readonly List<string> _createdProfileIds = new();

    public ProfileServiceTests()
    {
        _service = new ProfileService(Logger.None);
    }

    [Fact]
    public async Task GetProfilesAsync_InitiallyDoesNotContainTestProfiles()
    {
        // We cannot guarantee a truly empty store because the real file may have
        // profiles from other runs. Instead, verify that the result is a valid list
        // and does not contain our unique test prefix.
        var profiles = await _service.GetProfilesAsync();

        Assert.NotNull(profiles);
        Assert.DoesNotContain(profiles, p => p.Name.StartsWith("__TEST_INIT__"));
    }

    [Fact]
    public async Task SaveProfileAsync_ThenGetProfileAsync_ReturnsTheSavedProfile()
    {
        var profile = CreateTestProfile("SaveAndGet");

        await _service.SaveProfileAsync(profile);
        _createdProfileIds.Add(profile.Id);

        var retrieved = await _service.GetProfileAsync(profile.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(profile.Id, retrieved.Id);
        Assert.Equal(profile.Name, retrieved.Name);
        Assert.Equal(profile.MonitorBrightness, retrieved.MonitorBrightness);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesTheProfile()
    {
        var profile = CreateTestProfile("DeleteTest");

        await _service.SaveProfileAsync(profile);
        _createdProfileIds.Add(profile.Id);

        await _service.DeleteProfileAsync(profile.Id);

        var retrieved = await _service.GetProfileAsync(profile.Id);
        Assert.Null(retrieved);

        // Already deleted, remove from cleanup list
        _createdProfileIds.Remove(profile.Id);
    }

    [Fact]
    public async Task CreateFromCurrentAsync_CreatesProfileWithCorrectBrightness()
    {
        var monitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                MonitorId = "TEST-MON-001",
                FriendlyName = "Test Monitor A",
                CurrentBrightness = 65,
                MinBrightness = 0,
                MaxBrightness = 100
            },
            new MonitorInfo
            {
                MonitorId = "TEST-MON-002",
                FriendlyName = "Test Monitor B",
                CurrentBrightness = 40,
                MinBrightness = 0,
                MaxBrightness = 100
            }
        };

        var profileName = $"__TEST_CreateFromCurrent_{Guid.NewGuid():N}";
        var profile = await _service.CreateFromCurrentAsync(profileName, monitors);
        _createdProfileIds.Add(profile.Id);

        Assert.Equal(profileName, profile.Name);
        Assert.Equal(2, profile.MonitorBrightness.Count);
        Assert.Equal(65, profile.MonitorBrightness["TEST-MON-001"]);
        Assert.Equal(40, profile.MonitorBrightness["TEST-MON-002"]);
    }

    [Fact]
    public async Task CreateFromCurrentAsync_ProfileIsPersisted()
    {
        var monitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                MonitorId = "TEST-MON-PERSIST",
                FriendlyName = "Persist Test",
                CurrentBrightness = 88,
                MinBrightness = 0,
                MaxBrightness = 100
            }
        };

        var profileName = $"__TEST_Persisted_{Guid.NewGuid():N}";
        var profile = await _service.CreateFromCurrentAsync(profileName, monitors);
        _createdProfileIds.Add(profile.Id);

        var retrieved = await _service.GetProfileAsync(profile.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(88, retrieved.MonitorBrightness["TEST-MON-PERSIST"]);
    }

    [Fact]
    public async Task SaveProfileAsync_UpdateExistingProfile()
    {
        var profile = CreateTestProfile("UpdateTest");

        await _service.SaveProfileAsync(profile);
        _createdProfileIds.Add(profile.Id);

        profile.MonitorBrightness["MON-A"] = 99;
        await _service.SaveProfileAsync(profile);

        var retrieved = await _service.GetProfileAsync(profile.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(99, retrieved.MonitorBrightness["MON-A"]);
    }

    [Fact]
    public async Task DeleteProfileAsync_NonExistentId_DoesNotThrow()
    {
        // Should complete without throwing
        await _service.DeleteProfileAsync("DOES-NOT-EXIST-" + Guid.NewGuid());
    }

    [Fact]
    public async Task GetProfileAsync_NonExistentId_ReturnsNull()
    {
        var result = await _service.GetProfileAsync("DOES-NOT-EXIST-" + Guid.NewGuid());

        Assert.Null(result);
    }

    private static BrightnessProfile CreateTestProfile(string label)
    {
        return new BrightnessProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"__TEST_{label}_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            MonitorBrightness = new Dictionary<string, int>
            {
                ["MON-A"] = 50,
                ["MON-B"] = 75
            }
        };
    }

    public void Dispose()
    {
        // Clean up any profiles we created during the test run
        foreach (var id in _createdProfileIds)
        {
            _service.DeleteProfileAsync(id).GetAwaiter().GetResult();
        }
    }
}
