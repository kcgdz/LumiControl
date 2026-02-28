# LumiControl

**Monitor Brightness Control for Windows** - Control brightness for all connected monitors using DDC/CI protocol.

Developed by [kcgdz](https://github.com/kcgdz)

---

## Features

- **Multi-Monitor Support** - Detect and control all connected monitors (internal + external)
- **DDC/CI Protocol** - Uses industry-standard DDC/CI via Windows Physical Monitor API
- **WMI Integration** - Controls internal laptop displays via WMI
- **Brightness Profiles** - Save and switch between named brightness configurations (Gaming, Night Mode, Work, etc.)
- **Scheduled Brightness** - Time-based automatic brightness rules with smooth transitions
- **Sunrise/Sunset Detection** - Automatically adjust brightness based on time of day
- **Global Hotkeys** - Configurable keyboard shortcuts for quick brightness changes
- **System Tray** - Runs minimized with quick access from the notification area
- **Monitor Hot-Plug** - Automatically detects when monitors are connected or disconnected
- **Modern Dark UI** - Beautiful dark theme with smooth animations
- **Mock Mode** - Test the app without real monitors using `--mock` flag

## Screenshots

*Coming soon*

## Requirements

- **OS**: Windows 10 / Windows 11
- **Runtime**: .NET 8.0 Runtime
- **Monitors**: DDC/CI compatible monitors for external display control

> **Note**: Not all monitors support DDC/CI. If your external monitor doesn't respond to brightness changes, check if DDC/CI is enabled in your monitor's OSD settings. Internal laptop displays are controlled via WMI and should always work.

## Installation

### Portable (no install)
1. Download `LumiControl-portable.zip` from [Releases](../../releases)
2. Extract to any folder
3. Run `LumiControl.exe`

### Self-Contained EXE
1. Download `LumiControl.exe` from [Releases](../../releases)
2. Run directly - no .NET runtime needed

### Build from Source
```bash
git clone https://github.com/your-username/LumiControl.git
cd LumiControl
dotnet restore
dotnet build
dotnet run --project LumiControl
```

## Usage

### Mock Mode (Testing)
Run without real monitors:
```bash
LumiControl.exe --mock
```

### Keyboard Shortcuts
Configure global hotkeys in Settings to quickly adjust brightness without opening the app.

### Profiles
1. Adjust brightness for each monitor
2. Click the profile dropdown and save current settings
3. Switch profiles from the system tray or main window

### Scheduled Brightness
Enable auto-schedule to have brightness change automatically based on time of day.

## Architecture

```
LumiControl.sln
├── LumiControl/              # WPF application (UI layer)
│   ├── Views/                # XAML windows
│   ├── ViewModels/           # MVVM view models
│   ├── Models/               # UI-specific models
│   ├── Services/             # App-level services
│   ├── Converters/           # XAML value converters
│   └── Resources/            # Themes, assets
├── LumiControl.Core/         # Core library
│   ├── Models/               # Domain models
│   ├── Services/             # Business logic services
│   └── Native/               # P/Invoke declarations
└── LumiControl.Tests/        # Unit tests (xUnit)
```

## DDC/CI Compatibility

DDC/CI (Display Data Channel / Command Interface) is a standard for communication between a computer and a monitor. Most modern monitors support it, but it may be disabled by default.

**To enable DDC/CI on your monitor:**
1. Open your monitor's OSD menu
2. Find "DDC/CI" setting (usually under System or Settings)
3. Set it to "On" or "Enabled"

**Known compatible connections:**
- HDMI
- DisplayPort
- DVI
- VGA (limited support)

**Known limitations:**
- USB-C/Thunderbolt docks may not pass DDC/CI commands
- Some gaming monitors disable DDC/CI during certain modes
- Virtual/remote displays are not supported

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

Developed with care by [kcgdz](https://github.com/kcgdz)
