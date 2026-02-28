using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using LumiControl.Core.Native;
using LumiControl.Core.Services;
using LumiControl.Services;
using LumiControl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace LumiControl.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
    private TaskbarIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _hotkeyService = App.Services.GetRequiredService<IHotkeyService>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Setup system tray
        SetupTrayIcon();

        // Setup hotkeys
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (hwndSource != null)
        {
            _hotkeyService.Initialize(hwndSource.Handle);
            hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                return _hotkeyService.HandleMessage(hwnd, msg, wParam, lParam, ref handled);
            });

            _hotkeyService.HotkeyPressed += OnHotkeyPressed;

            if (_settingsService.Settings.Hotkeys != null)
                _hotkeyService.RegisterBrightnessHotkeys(_settingsService.Settings.Hotkeys);
        }

        // Listen for display changes
        if (hwndSource != null)
        {
            hwndSource.AddHook(WndProc);
        }

        // Initialize viewmodel
        await _viewModel.InitializeAsync();

        // Start minimized if configured
        if (_settingsService.Settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            if (_settingsService.Settings.MinimizeToTray)
                Hide();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            Log.Information("Display configuration changed, refreshing monitors");
            _ = _viewModel.RefreshMonitorsCommand.ExecuteAsync(null);
        }
        return IntPtr.Zero;
    }

    private void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        var step = _settingsService.Settings.Hotkeys.BrightnessStep;
        var delta = action.ActionType == HotkeyActionType.BrightnessUp ? step : -step;
        _viewModel.AdjustBrightness(delta, action.MonitorId);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "LumiControl - Monitor Brightness Control",
        };

        _trayIcon.TrayMouseDoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show LumiControl" };
        showItem.Click += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Refresh Monitors" };
        refreshItem.Click += (_, _) => _ = _viewModel.RefreshMonitorsCommand.ExecuteAsync(null);
        contextMenu.Items.Add(refreshItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) =>
        {
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        };
        contextMenu.Items.Add(quitItem);

        _trayIcon.ContextMenu = contextMenu;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settingsService.Settings.MinimizeToTray)
        {
            Hide();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyService.UnregisterAll();
        _trayIcon?.Dispose();
        Application.Current.Shutdown();
    }

    // Custom titlebar dragging
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
