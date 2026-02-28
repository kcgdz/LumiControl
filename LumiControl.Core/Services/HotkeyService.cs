using System;
using System.Collections.Generic;
using LumiControl.Core.Models;
using LumiControl.Core.Native;
using Serilog;

namespace LumiControl.Core.Services
{
    public enum HotkeyActionType
    {
        BrightnessUp,
        BrightnessDown
    }

    public class HotkeyAction : EventArgs
    {
        public HotkeyActionType ActionType { get; }
        public string? MonitorId { get; }

        public HotkeyAction(HotkeyActionType actionType, string? monitorId = null)
        {
            ActionType = actionType;
            MonitorId = monitorId;
        }
    }

    public interface IHotkeyService : IDisposable
    {
        void Initialize(IntPtr windowHandle);
        void RegisterBrightnessHotkeys(HotkeySettings settings);
        void UnregisterAll();
        IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);
        event EventHandler<HotkeyAction>? HotkeyPressed;
    }

    public class HotkeyService : IHotkeyService
    {
        private const int BaseHotkeyId = 0x7000;
        private const int GlobalBrightnessUpId = BaseHotkeyId;
        private const int GlobalBrightnessDownId = BaseHotkeyId + 1;
        private const int PerMonitorBaseId = BaseHotkeyId + 100;

        private readonly ILogger _logger;
        private IntPtr _windowHandle;
        private bool _initialized;
        private bool _disposed;

        private readonly Dictionary<int, HotkeyAction> _registeredHotkeys = new();
        private readonly List<int> _registeredIds = new();
        private int _nextPerMonitorId = PerMonitorBaseId;

        public event EventHandler<HotkeyAction>? HotkeyPressed;

        public HotkeyService(ILogger logger)
        {
            _logger = logger.ForContext<HotkeyService>();
        }

        public void Initialize(IntPtr windowHandle)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotkeyService));

            _windowHandle = windowHandle;
            _initialized = true;
            _logger.Information("HotkeyService initialized with window handle {Handle}", windowHandle);
        }

        public void RegisterBrightnessHotkeys(HotkeySettings settings)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HotkeyService));
            if (!_initialized)
                throw new InvalidOperationException("HotkeyService must be initialized before registering hotkeys. Call Initialize() first.");

            UnregisterAll();

            if (settings.BrightnessUp != null)
            {
                RegisterHotkey(
                    GlobalBrightnessUpId,
                    settings.BrightnessUp,
                    new HotkeyAction(HotkeyActionType.BrightnessUp));
            }

            if (settings.BrightnessDown != null)
            {
                RegisterHotkey(
                    GlobalBrightnessDownId,
                    settings.BrightnessDown,
                    new HotkeyAction(HotkeyActionType.BrightnessDown));
            }

            foreach (var (monitorId, binding) in settings.PerMonitorUp)
            {
                int id = _nextPerMonitorId++;
                RegisterHotkey(
                    id,
                    binding,
                    new HotkeyAction(HotkeyActionType.BrightnessUp, monitorId));
            }

            foreach (var (monitorId, binding) in settings.PerMonitorDown)
            {
                int id = _nextPerMonitorId++;
                RegisterHotkey(
                    id,
                    binding,
                    new HotkeyAction(HotkeyActionType.BrightnessDown, monitorId));
            }

            _logger.Information("Registered {Count} brightness hotkeys (step: {Step}%)",
                _registeredIds.Count, settings.BrightnessStep);
        }

        public void UnregisterAll()
        {
            if (_disposed)
                return;

            foreach (int id in _registeredIds)
            {
                if (!NativeMethods.UnregisterHotKey(_windowHandle, id))
                {
                    _logger.Warning("Failed to unregister hotkey with id {Id}", id);
                }
            }

            int count = _registeredIds.Count;
            _registeredIds.Clear();
            _registeredHotkeys.Clear();
            _nextPerMonitorId = PerMonitorBaseId;

            if (count > 0)
            {
                _logger.Information("Unregistered {Count} hotkeys", count);
            }
        }

        public IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != NativeMethods.WM_HOTKEY)
                return IntPtr.Zero;

            int hotkeyId = wParam.ToInt32();

            if (_registeredHotkeys.TryGetValue(hotkeyId, out var action))
            {
                handled = true;
                _logger.Debug("Hotkey triggered: {ActionType}, Monitor: {MonitorId}",
                    action.ActionType, action.MonitorId ?? "all");
                HotkeyPressed?.Invoke(this, action);
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnregisterAll();
            _logger.Information("HotkeyService disposed");
        }

        private void RegisterHotkey(int id, HotkeyBinding binding, HotkeyAction action)
        {
            uint modifiers = (uint)binding.Modifiers | NativeMethods.MOD_NOREPEAT;
            uint keyCode = (uint)binding.KeyCode;

            bool success = NativeMethods.RegisterHotKey(_windowHandle, id, modifiers, keyCode);

            if (success)
            {
                _registeredHotkeys[id] = action;
                _registeredIds.Add(id);
                _logger.Debug("Registered hotkey: {DisplayName} (id={Id}, action={Action}, monitor={Monitor})",
                    binding.DisplayName, id, action.ActionType, action.MonitorId ?? "all");
            }
            else
            {
                _logger.Error("Failed to register hotkey: {DisplayName} (keyCode=0x{KeyCode:X}, modifiers=0x{Modifiers:X}). " +
                              "The key combination may already be in use by another application.",
                    binding.DisplayName, keyCode, modifiers);
            }
        }
    }
}
