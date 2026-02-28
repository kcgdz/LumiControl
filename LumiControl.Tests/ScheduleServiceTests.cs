using LumiControl.Core.Models;
using LumiControl.Core.Services;
using Serilog.Core;
using Xunit;

namespace LumiControl.Tests;

public class ScheduleServiceTests : IDisposable
{
    private readonly MockMonitorService _mockMonitorService;
    private readonly ScheduleService _service;

    public ScheduleServiceTests()
    {
        _mockMonitorService = new MockMonitorService(Logger.None);
        _service = new ScheduleService(_mockMonitorService, Logger.None);
    }

    [Fact]
    public void EvaluateTargetBrightness_NoRules_ReturnsNegativeOne()
    {
        int result = _service.EvaluateTargetBrightness("MOCK-INTERNAL-001");

        Assert.Equal(-1, result);
    }

    [Fact]
    public void EvaluateTargetBrightness_SingleRuleActiveNow_ReturnsItsBrightness()
    {
        // Create a rule that started one hour ago and covers today
        var now = DateTime.Now;
        var oneHourAgo = TimeOnly.FromDateTime(now.AddHours(-1));

        var rule = new ScheduleRule
        {
            Id = "test-rule-active",
            Name = "Active Now Rule",
            IsEnabled = true,
            StartTime = oneHourAgo,
            Brightness = 75,
            TransitionMinutes = 0, // No transition, so brightness is exact
            MonitorId = null, // Applies to all monitors
            Days = Enum.GetValues<DayOfWeek>().ToList()
        };

        _service.AddRule(rule);

        int result = _service.EvaluateTargetBrightness("MOCK-INTERNAL-001");

        Assert.Equal(75, result);
    }

    [Fact]
    public void AddRule_ThenGetRules_ContainsTheAddedRule()
    {
        var rule = new ScheduleRule
        {
            Id = "test-add-rule",
            Name = "Test Add",
            IsEnabled = true,
            StartTime = new TimeOnly(9, 0),
            Brightness = 60,
            TransitionMinutes = 10,
            MonitorId = null,
            Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday }
        };

        _service.AddRule(rule);

        var rules = _service.GetRules();

        Assert.Single(rules);
        Assert.Equal("test-add-rule", rules[0].Id);
        Assert.Equal("Test Add", rules[0].Name);
        Assert.Equal(60, rules[0].Brightness);
    }

    [Fact]
    public void RemoveRule_RemovesTheCorrectRule()
    {
        var rule1 = new ScheduleRule
        {
            Id = "rule-to-keep",
            Name = "Keep Me",
            IsEnabled = true,
            StartTime = new TimeOnly(8, 0),
            Brightness = 50
        };

        var rule2 = new ScheduleRule
        {
            Id = "rule-to-remove",
            Name = "Remove Me",
            IsEnabled = true,
            StartTime = new TimeOnly(20, 0),
            Brightness = 30
        };

        _service.AddRule(rule1);
        _service.AddRule(rule2);

        _service.RemoveRule("rule-to-remove");

        var rules = _service.GetRules();

        Assert.Single(rules);
        Assert.Equal("rule-to-keep", rules[0].Id);
    }

    [Fact]
    public void GetRules_ReturnsAllAddedRules()
    {
        var rule1 = new ScheduleRule
        {
            Id = "rule-1",
            Name = "Morning",
            IsEnabled = true,
            StartTime = new TimeOnly(7, 0),
            Brightness = 80
        };

        var rule2 = new ScheduleRule
        {
            Id = "rule-2",
            Name = "Evening",
            IsEnabled = true,
            StartTime = new TimeOnly(19, 0),
            Brightness = 30
        };

        var rule3 = new ScheduleRule
        {
            Id = "rule-3",
            Name = "Night",
            IsEnabled = true,
            StartTime = new TimeOnly(23, 0),
            Brightness = 10
        };

        _service.AddRule(rule1);
        _service.AddRule(rule2);
        _service.AddRule(rule3);

        var rules = _service.GetRules();

        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, r => r.Id == "rule-1");
        Assert.Contains(rules, r => r.Id == "rule-2");
        Assert.Contains(rules, r => r.Id == "rule-3");
    }

    [Fact]
    public void EvaluateTargetBrightness_DisabledRule_IsIgnored()
    {
        var now = DateTime.Now;
        var oneHourAgo = TimeOnly.FromDateTime(now.AddHours(-1));

        var disabledRule = new ScheduleRule
        {
            Id = "disabled-rule",
            Name = "Disabled",
            IsEnabled = false,
            StartTime = oneHourAgo,
            Brightness = 90,
            TransitionMinutes = 0,
            MonitorId = null,
            Days = Enum.GetValues<DayOfWeek>().ToList()
        };

        _service.AddRule(disabledRule);

        int result = _service.EvaluateTargetBrightness("MOCK-INTERNAL-001");

        Assert.Equal(-1, result);
    }

    [Fact]
    public void EvaluateTargetBrightness_RuleForDifferentDay_IsIgnored()
    {
        var now = DateTime.Now;
        var today = now.DayOfWeek;
        var oneHourAgo = TimeOnly.FromDateTime(now.AddHours(-1));

        // Pick a day that is definitely not today
        var differentDay = today == DayOfWeek.Monday ? DayOfWeek.Tuesday : DayOfWeek.Monday;

        var rule = new ScheduleRule
        {
            Id = "wrong-day-rule",
            Name = "Wrong Day",
            IsEnabled = true,
            StartTime = oneHourAgo,
            Brightness = 60,
            TransitionMinutes = 0,
            MonitorId = null,
            Days = new List<DayOfWeek> { differentDay }
        };

        _service.AddRule(rule);

        int result = _service.EvaluateTargetBrightness("MOCK-INTERNAL-001");

        Assert.Equal(-1, result);
    }

    [Fact]
    public void EvaluateTargetBrightness_MonitorSpecificRule_AppliesToCorrectMonitor()
    {
        var now = DateTime.Now;
        var oneHourAgo = TimeOnly.FromDateTime(now.AddHours(-1));

        var rule = new ScheduleRule
        {
            Id = "monitor-specific",
            Name = "Only for DELL",
            IsEnabled = true,
            StartTime = oneHourAgo,
            Brightness = 45,
            TransitionMinutes = 0,
            MonitorId = "MOCK-DELL-002",
            Days = Enum.GetValues<DayOfWeek>().ToList()
        };

        _service.AddRule(rule);

        int resultForDell = _service.EvaluateTargetBrightness("MOCK-DELL-002");
        int resultForLg = _service.EvaluateTargetBrightness("MOCK-LG-003");

        Assert.Equal(45, resultForDell);
        Assert.Equal(-1, resultForLg);
    }

    [Fact]
    public void RemoveRule_NonExistentId_DoesNotThrow()
    {
        // Should not throw for a missing rule
        _service.RemoveRule("non-existent-id");

        var rules = _service.GetRules();
        Assert.Empty(rules);
    }

    [Fact]
    public void GetRules_InitiallyEmpty()
    {
        var rules = _service.GetRules();

        Assert.Empty(rules);
    }

    [Fact]
    public void GetRules_ReturnsACopy_NotTheInternalList()
    {
        var rule = new ScheduleRule
        {
            Id = "copy-test",
            Name = "Copy Test",
            IsEnabled = true,
            StartTime = new TimeOnly(12, 0),
            Brightness = 50
        };

        _service.AddRule(rule);

        var rules = _service.GetRules();
        rules.Clear(); // Clearing the returned list should not affect internal state

        var rulesAgain = _service.GetRules();
        Assert.Single(rulesAgain);
    }

    public void Dispose()
    {
        _service.Dispose();
        _mockMonitorService.Dispose();
    }
}
