using System.Text.Json;
using System.Text.Json.Serialization;
using LumiControl.Core.Models;
using Serilog;

namespace LumiControl.Core.Services;

/// <summary>
/// Defines the contract for a time-based schedule service that manages automatic
/// brightness adjustments based on user-defined rules and sunrise/sunset calculations.
/// </summary>
public interface IScheduleService
{
    /// <summary>Starts the periodic evaluation timer.</summary>
    Task StartAsync();

    /// <summary>Stops the periodic evaluation timer and cancels any pending operations.</summary>
    Task StopAsync();

    /// <summary>Returns a snapshot of all configured schedule rules.</summary>
    List<ScheduleRule> GetRules();

    /// <summary>Adds a new rule to the schedule.</summary>
    void AddRule(ScheduleRule rule);

    /// <summary>Removes a rule by its unique identifier.</summary>
    void RemoveRule(string ruleId);

    /// <summary>Replaces an existing rule (matched by <see cref="ScheduleRule.Id"/>).</summary>
    void UpdateRule(ScheduleRule rule);

    /// <summary>
    /// Evaluates what the brightness should be right now for the given monitor,
    /// taking into account all enabled rules, transition periods, and sunrise/sunset.
    /// Returns -1 if no rule applies.
    /// </summary>
    int EvaluateTargetBrightness(string? monitorId);

    /// <summary>Persists the current schedule store to disk.</summary>
    Task SaveAsync();

    /// <summary>Loads the schedule store from disk.</summary>
    Task LoadAsync();

    /// <summary>Raised when a rule is triggered and brightness is applied.</summary>
    event EventHandler<ScheduleRule>? RuleTriggered;
}

/// <summary>
/// Default implementation of <see cref="IScheduleService"/>. Uses a one-minute periodic timer
/// to evaluate rules, applies smooth brightness transitions, and supports optional
/// sunrise/sunset-based automation via a basic solar position algorithm.
/// </summary>
public class ScheduleService : IScheduleService, IDisposable
{
    private readonly ILogger _logger;
    private readonly IMonitorService _monitorService;
    private readonly string _filePath;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    private ScheduleStore _store = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _timerLoop;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<ScheduleRule>? RuleTriggered;

    public ScheduleService(IMonitorService monitorService, ILogger logger)
    {
        _monitorService = monitorService;
        _logger = logger.ForContext<ScheduleService>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(appData, "LumiControl", "schedule.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        _logger.Information("ScheduleService initialized, data path: {FilePath}", _filePath);
    }

    // ───────────────────────────── Lifecycle ─────────────────────────────

    /// <inheritdoc />
    public async Task StartAsync()
    {
        ThrowIfDisposed();

        _logger.Information("Starting schedule service...");

        await LoadAsync();

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        _timerLoop = RunTimerLoopAsync(_cts.Token);

        // Perform an immediate evaluation so we don't wait a full minute on startup.
        _ = Task.Run(() => EvaluateAndApplyAsync(_cts.Token));

        _logger.Information("Schedule service started with {RuleCount} rules", _store.Rules.Count);
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        _logger.Information("Stopping schedule service...");

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_timerLoop is not null)
        {
            try
            {
                await _timerLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _timer?.Dispose();
        _timer = null;
        _cts?.Dispose();
        _cts = null;
        _timerLoop = null;

        _logger.Information("Schedule service stopped");
    }

    // ──────────────────────────── Rule CRUD ──────────────────────────────

    /// <inheritdoc />
    public List<ScheduleRule> GetRules()
    {
        lock (_lock)
        {
            return new List<ScheduleRule>(_store.Rules);
        }
    }

    /// <inheritdoc />
    public void AddRule(ScheduleRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_lock)
        {
            _store.Rules.Add(rule);
        }

        _logger.Information("Rule added: {RuleName} ({RuleId}), brightness {Brightness} at {Time}",
            rule.Name, rule.Id, rule.Brightness, rule.StartTime);
    }

    /// <inheritdoc />
    public void RemoveRule(string ruleId)
    {
        lock (_lock)
        {
            int removed = _store.Rules.RemoveAll(r => r.Id == ruleId);
            if (removed > 0)
            {
                _logger.Information("Rule removed: {RuleId}", ruleId);
            }
            else
            {
                _logger.Warning("Attempted to remove non-existent rule: {RuleId}", ruleId);
            }
        }
    }

    /// <inheritdoc />
    public void UpdateRule(ScheduleRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_lock)
        {
            int index = _store.Rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0)
            {
                _store.Rules[index] = rule;
                _logger.Information("Rule updated: {RuleName} ({RuleId})", rule.Name, rule.Id);
            }
            else
            {
                _logger.Warning("Attempted to update non-existent rule: {RuleId}", rule.Id);
            }
        }
    }

    // ──────────────────────── Brightness Evaluation ─────────────────────

    /// <inheritdoc />
    public int EvaluateTargetBrightness(string? monitorId)
    {
        var now = DateTime.Now;
        var currentTime = TimeOnly.FromDateTime(now);
        var today = now.DayOfWeek;

        // Collect applicable rules: enabled, matching day, matching monitor.
        List<ScheduleRule> applicableRules;
        bool useSunriseSunset;
        int sunriseBrightness, sunsetBrightness;
        double latitude, longitude;

        lock (_lock)
        {
            applicableRules = _store.Rules
                .Where(r => r.IsEnabled)
                .Where(r => r.Days.Contains(today))
                .Where(r => r.MonitorId is null || r.MonitorId == monitorId)
                .OrderBy(r => r.StartTime)
                .ToList();

            useSunriseSunset = _store.UseSunriseSunset;
            sunriseBrightness = _store.SunriseBrightness;
            sunsetBrightness = _store.SunsetBrightness;
            latitude = _store.Latitude;
            longitude = _store.Longitude;
        }

        // If sunrise/sunset mode is active, inject synthetic rules for today.
        if (useSunriseSunset)
        {
            var (sunrise, sunset) = CalculateSunriseSunset(now, latitude, longitude);

            var sunriseRule = new ScheduleRule
            {
                Id = "__sunrise__",
                Name = "Sunrise",
                IsEnabled = true,
                StartTime = sunrise,
                Brightness = sunriseBrightness,
                TransitionMinutes = 30,
                MonitorId = null,
                Days = Enum.GetValues<DayOfWeek>().ToList()
            };

            var sunsetRule = new ScheduleRule
            {
                Id = "__sunset__",
                Name = "Sunset",
                IsEnabled = true,
                StartTime = sunset,
                Brightness = sunsetBrightness,
                TransitionMinutes = 30,
                MonitorId = null,
                Days = Enum.GetValues<DayOfWeek>().ToList()
            };

            applicableRules.Add(sunriseRule);
            applicableRules.Add(sunsetRule);
            applicableRules = applicableRules.OrderBy(r => r.StartTime).ToList();
        }

        if (applicableRules.Count == 0)
        {
            return -1;
        }

        // Find the most recently started rule (the one whose StartTime is <= now).
        // If no rule has started yet today, wrap around to the last rule of the day
        // (which effectively started "yesterday" and is still in effect).
        ScheduleRule? activeRule = null;
        ScheduleRule? previousRule = null;

        for (int i = applicableRules.Count - 1; i >= 0; i--)
        {
            if (applicableRules[i].StartTime <= currentTime)
            {
                activeRule = applicableRules[i];
                previousRule = i > 0 ? applicableRules[i - 1] : applicableRules[^1];
                break;
            }
        }

        // No rule has started yet today — wrap around to the last rule.
        activeRule ??= applicableRules[^1];
        previousRule ??= applicableRules.Count > 1 ? applicableRules[^2] : activeRule;

        // Check if we are within the transition window of the active rule.
        int elapsedMinutes = MinutesBetween(activeRule.StartTime, currentTime);

        if (activeRule.TransitionMinutes > 0 && elapsedMinutes < activeRule.TransitionMinutes)
        {
            // Interpolate between the previous rule's brightness and this rule's target.
            double progress = (double)elapsedMinutes / activeRule.TransitionMinutes;
            progress = SmoothStep(progress);

            int fromBrightness = previousRule.Brightness;
            int toBrightness = activeRule.Brightness;
            int interpolated = (int)Math.Round(fromBrightness + (toBrightness - fromBrightness) * progress);

            return Math.Clamp(interpolated, 0, 100);
        }

        return Math.Clamp(activeRule.Brightness, 0, 100);
    }

    // ─────────────────────── Persistence ─────────────────────────────────

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        ThrowIfDisposed();

        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.Debug("Created directory: {Directory}", directory);
            }

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_store, _jsonOptions);
            }

            await File.WriteAllTextAsync(_filePath, json);
            _logger.Information("Schedule saved to {FilePath} ({RuleCount} rules)",
                _filePath, _store.Rules.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save schedule to {FilePath}", _filePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        ThrowIfDisposed();

        if (!File.Exists(_filePath))
        {
            _logger.Information("No schedule file found at {FilePath}, starting with empty schedule", _filePath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var loaded = JsonSerializer.Deserialize<ScheduleStore>(json, _jsonOptions);

            if (loaded is not null)
            {
                lock (_lock)
                {
                    _store = loaded;
                }

                _logger.Information("Schedule loaded from {FilePath} ({RuleCount} rules)",
                    _filePath, _store.Rules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load schedule from {FilePath}, starting with empty schedule", _filePath);
            lock (_lock)
            {
                _store = new ScheduleStore();
            }
        }
    }

    // ──────────────────────── Timer Loop ─────────────────────────────────

    private async Task RunTimerLoopAsync(CancellationToken cancellationToken)
    {
        _logger.Debug("Timer loop started");

        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken))
            {
                await EvaluateAndApplyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Debug("Timer loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error in timer loop");
        }
    }

    private async Task EvaluateAndApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitors = await _monitorService.DetectMonitorsAsync();

            foreach (var monitor in monitors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int targetBrightness = EvaluateTargetBrightness(monitor.MonitorId);

                if (targetBrightness < 0)
                {
                    continue;
                }

                int currentBrightness = await _monitorService.GetBrightnessAsync(monitor.MonitorId);

                if (currentBrightness == targetBrightness)
                {
                    continue;
                }

                _logger.Debug(
                    "Adjusting monitor {MonitorId} from {Current} to {Target}",
                    monitor.MonitorId, currentBrightness, targetBrightness);

                bool success = await _monitorService.SetBrightnessAsync(monitor.MonitorId, targetBrightness);

                if (success)
                {
                    // Find the active rule to raise the event.
                    var triggeredRule = FindActiveRule(monitor.MonitorId);
                    if (triggeredRule is not null)
                    {
                        RuleTriggered?.Invoke(this, triggeredRule);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown, do not log as error.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during scheduled brightness evaluation");
        }
    }

    /// <summary>
    /// Finds the rule that is currently driving brightness for the given monitor.
    /// </summary>
    private ScheduleRule? FindActiveRule(string? monitorId)
    {
        var currentTime = TimeOnly.FromDateTime(DateTime.Now);
        var today = DateTime.Now.DayOfWeek;

        lock (_lock)
        {
            return _store.Rules
                .Where(r => r.IsEnabled)
                .Where(r => r.Days.Contains(today))
                .Where(r => r.MonitorId is null || r.MonitorId == monitorId)
                .OrderBy(r => r.StartTime)
                .LastOrDefault(r => r.StartTime <= currentTime)
                ?? _store.Rules
                    .Where(r => r.IsEnabled)
                    .Where(r => r.Days.Contains(today))
                    .Where(r => r.MonitorId is null || r.MonitorId == monitorId)
                    .OrderBy(r => r.StartTime)
                    .LastOrDefault();
        }
    }

    // ────────────────────── Solar Calculation ────────────────────────────

    /// <summary>
    /// Computes approximate sunrise and sunset times for a given date and geographic
    /// location using a simplified solar position algorithm. Accuracy is within a few
    /// minutes, which is sufficient for brightness scheduling.
    /// Reference: US Naval Observatory / NOAA simplified equations.
    /// </summary>
    private static (TimeOnly Sunrise, TimeOnly Sunset) CalculateSunriseSunset(
        DateTime date, double latitude, double longitude)
    {
        // Day of year
        int dayOfYear = date.DayOfYear;

        // Fractional year (gamma) in radians
        double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1 + (12.0 - 12.0) / 24.0);

        // Equation of time (minutes)
        double eqTime = 229.18 * (
            0.000075
            + 0.001868 * Math.Cos(gamma)
            - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2.0 * gamma)
            - 0.040849 * Math.Sin(2.0 * gamma));

        // Solar declination (radians)
        double decl =
            0.006918
            - 0.399912 * Math.Cos(gamma)
            + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2.0 * gamma)
            + 0.000907 * Math.Sin(2.0 * gamma)
            - 0.002697 * Math.Cos(3.0 * gamma)
            + 0.00148 * Math.Sin(3.0 * gamma);

        double latRad = latitude * Math.PI / 180.0;

        // Hour angle for sunrise/sunset (solar zenith = 90.833 degrees for atmospheric refraction)
        double zenith = 90.833 * Math.PI / 180.0;
        double cosHourAngle = (Math.Cos(zenith) / (Math.Cos(latRad) * Math.Cos(decl)))
                              - Math.Tan(latRad) * Math.Tan(decl);

        // Clamp for polar regions where the sun may not rise or set.
        cosHourAngle = Math.Clamp(cosHourAngle, -1.0, 1.0);

        double hourAngle = Math.Acos(cosHourAngle) * 180.0 / Math.PI; // back to degrees

        // Sunrise and sunset in minutes from midnight UTC
        double sunriseMinutesUtc = 720.0 - 4.0 * (longitude + hourAngle) - eqTime;
        double sunsetMinutesUtc = 720.0 - 4.0 * (longitude - hourAngle) - eqTime;

        // Convert UTC minutes to local time via the system's current UTC offset.
        double utcOffsetMinutes = TimeZoneInfo.Local.GetUtcOffset(date).TotalMinutes;

        double sunriseLocal = sunriseMinutesUtc + utcOffsetMinutes;
        double sunsetLocal = sunsetMinutesUtc + utcOffsetMinutes;

        // Normalize to [0, 1440)
        sunriseLocal = ((sunriseLocal % 1440.0) + 1440.0) % 1440.0;
        sunsetLocal = ((sunsetLocal % 1440.0) + 1440.0) % 1440.0;

        var sunrise = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(sunriseLocal));
        var sunset = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(sunsetLocal));

        return (sunrise, sunset);
    }

    // ─────────────────────── Helper Methods ─────────────────────────────

    /// <summary>
    /// Returns the number of minutes elapsed from <paramref name="from"/> to
    /// <paramref name="to"/>, accounting for forward-only passage of time within
    /// the same day. If <paramref name="to"/> is earlier than <paramref name="from"/>
    /// (i.e. wrapped past midnight), this returns the forward distance.
    /// </summary>
    private static int MinutesBetween(TimeOnly from, TimeOnly to)
    {
        int diff = (int)(to.ToTimeSpan() - from.ToTimeSpan()).TotalMinutes;
        return diff >= 0 ? diff : diff + 1440;
    }

    /// <summary>
    /// Hermite smooth-step interpolation (3t^2 - 2t^3) for a more natural
    /// brightness transition feel compared to linear interpolation.
    /// </summary>
    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    // ─────────────────────────── Dispose ─────────────────────────────────

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.Information("Disposing ScheduleService");

        _cts?.Cancel();
        _timer?.Dispose();
        _cts?.Dispose();

        _disposed = true;
    }
}
