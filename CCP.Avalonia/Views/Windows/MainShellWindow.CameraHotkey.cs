using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private IDisposable? _cameraRegistration;
    private DispatcherTimer? _cameraBindingWatch;
    private (string Key, uint Mods, bool Global)? _cameraBinding;
    private KeyBinding? _cameraKeyBinding;
    private bool _cameraClosed;
    internal Func<Window, string, uint, Action, IDisposable?> RegisterCamera = CameraGlobalHotkey.Register;
    internal bool CameraPlatformSupported = OperatingSystem.IsWindows();
    internal bool CameraHotkeyArmed => _cameraRegistration != null;

    // WPF MainWindow.SessionIO.cs:1324. The timer also catches settings loaded by a preset.
    internal void StartCameraHotkey()
    {
        if (_cameraBindingWatch != null || _cameraClosed) return;
        ApplyCameraHotkey();
        _cameraBindingWatch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => ApplyCameraHotkey());
        _cameraBindingWatch.Start();
        Closed += (_, _) => StopCameraHotkey();
    }

    internal static (string Key, uint Mods) CameraChord()
    {
        var s = CoreSettings.Current.CompanionPrompt;
        string name = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s.CameraShortcutKey;
        if (!HotkeyNames.TryParse(name, out var key)) key = Key.K;
        uint mods = 0;
        foreach (var part in (s?.CameraShortcutModifiers ?? "Control,Alt").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            mods |= part.ToLowerInvariant() switch { "alt" => 1u, "control" or "ctrl" => 2u, "shift" => 4u, "windows" or "win" or "meta" => 8u, _ => 0u };
        return (key.ToString(), mods == 0 ? 3u : mods);
    }

    internal void ApplyCameraHotkey()
    {
        var (key, mods) = CameraChord();
        var s = CoreSettings.Current;
        var binding = (key, mods, s.CompanionPrompt?.CameraShortcutGlobal != false);
        if (_cameraBinding == binding) return;
        _cameraBinding = binding;
        _cameraRegistration?.Dispose();
        _cameraRegistration = null;
        if (_cameraKeyBinding != null) KeyBindings.Remove(_cameraKeyBinding);
        var localMods = (mods & 1) != 0 ? KeyModifiers.Alt : KeyModifiers.None;
        if ((mods & 2) != 0) localMods |= KeyModifiers.Control;
        if ((mods & 4) != 0) localMods |= KeyModifiers.Shift;
        if ((mods & 8) != 0) localMods |= KeyModifiers.Meta;
        _cameraKeyBinding = new KeyBinding { Gesture = new KeyGesture(Enum.Parse<Key>(key), localMods),
            Command = new NavSectionCommand(ToggleCameraFromHotkey) };
        KeyBindings.Insert(0, _cameraKeyBinding);
        if (_cameraClosed || !CameraPlatformSupported || !binding.Item3) return;
        // Camera registers first in WPF. Release our Quick Recal registration on a rebind
        // to its chord, then let Quick Recal retry after the camera attempt.
        if (HotkeyNames.Matches(Key.G, key) && mods == 3 && _quickRecalRegistration != null)
        {
            _quickRecalRegistration.Dispose();
            _quickRecalRegistration = null;
            _quickRecalBinding = null;
        }
        try
        {
            _cameraRegistration = RegisterCamera(this, key, mods, () => Dispatcher.UIThread.Post(() =>
            {
                // A queued message from an old registration must not toggle a newly rebound camera.
                if (!_cameraClosed && _cameraBinding == binding && CameraChord() == (key, mods)
                    && CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal != false)
                    ToggleCameraFromHotkey();
            }));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey registration failed"); }
    }

    internal void ToggleCameraFromHotkey()
    {
        if (!WebcamTracker.Instance.IsRunning && !WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            try
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (!IsVisible) Show();
                Activate();
            }
            catch (Exception ex) { Log.Warning(ex, "Camera hotkey window restore failed"); }
        }
        var tab = this.FindControl<BlinkTrainerTabView>("BlinkTrainerTab");
        if (tab != null) _ = tab.ToggleWebcamTrackingAsync(announce: true);
    }

    internal void StopCameraHotkey()
    {
        _cameraClosed = true;
        _cameraBindingWatch?.Stop();
        _cameraBindingWatch = null;
        _cameraRegistration?.Dispose();
        _cameraRegistration = null;
        if (_cameraKeyBinding != null) KeyBindings.Remove(_cameraKeyBinding);
        _cameraKeyBinding = null;
    }
}
