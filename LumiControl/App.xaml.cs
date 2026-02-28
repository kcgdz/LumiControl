using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using LumiControl.Core.Models;
using LumiControl.Core.Services;
using LumiControl.Services;
using LumiControl.ViewModels;
using Serilog;

namespace LumiControl;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LumiControl");
        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(Path.Combine(appDataPath, "logs"));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();

        Log.Information("LumiControl starting up");

        var useMock = e.Args.Contains("--mock");

        var services = new ServiceCollection();
        ConfigureServices(services, useMock);
        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        settingsService.LoadAsync().Wait();

        if (settingsService.Settings.UseMockMonitors)
            useMock = true;
    }

    private void ConfigureServices(IServiceCollection services, bool useMock)
    {
        services.AddSingleton(Log.Logger);

        if (useMock)
            services.AddSingleton<IMonitorService, MockMonitorService>();
        else
            services.AddSingleton<IMonitorService, MonitorService>();

        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<MainViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("LumiControl shutting down");

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
