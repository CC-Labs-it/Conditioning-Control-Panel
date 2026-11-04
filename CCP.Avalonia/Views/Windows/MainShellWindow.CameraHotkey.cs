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
    private (string Key, uint Mods, bool Global, bool Recal)? _cameraBinding;
    private bool _cameraClosed;
    internal Func<Window, string, uint, Action, IDisposable?> RegisterCamera = CameraGlobalHotkey.Register;
    internal bool CameraPlatformSupported = OperatingSystem.IsWindows();
    internal bool CameraHotkeyArmed => _cameraRegistration != null;

    // WPF MainWindow.SessionIO.cs:1324. The timer also catches settings loaded by a preset.
    internal void StartCameraHotkey()
    {
        if (!CameraPlatformSupported || _cameraBindingWatch != null || _cameraClosed) return;
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
        var binding = (key, mods, s.CompanionPrompt?.CameraShortcutGlobal != false, s.WebcamQuickRecalHotkeyEnabled);
        if (_cameraBinding == binding) return;
        _cameraBinding = binding;
        _cameraRegistration?.Dispose();
        _cameraRegistration = null;
        // Reserve Quick Recal's chord even if the native registration has failed.
        if (_cameraClosed || !binding.Item3 || (binding.Item4 && HotkeyNames.Matches(Key.G, key) && mods == 3)) return;
        try
        {
            _cameraRegistration = RegisterCamera(this, key, mods, () => Dispatcher.UIThread.Post(() =>
            {
                // A queued message from an old registration must not toggle a newly rebound camera.
                if (!_cameraClosed && _cameraBinding == binding && CameraChord() == (key, mods)
                    && CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal != false
                    && !(CoreSettings.Current.WebcamQuickRecalHotkeyEnabled && HotkeyNames.Matches(Key.G, key) && mods == 3))
                    ToggleCameraFromHotkey();
            }));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey registration failed"); }
    }

    internal void ToggleCameraFromHotkey()
    {
        if (!WebcamTracker.Instance.IsRunning && !WebcamConsent.IsCurrent(CoreSettings.Current))
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!IsVisible) Show();
            Activate();
        }
        this.FindControl<BlinkTrainerTabView>("BlinkTrainerTab")?.BtnBlinkTrainerStartStopTracker_Click(null, null!);
    }

    internal void StopCameraHotkey()
    {
        _cameraClosed = true;
        _cameraBindingWatch?.Stop();
        _cameraBindingWatch = null;
        _cameraRegistration?.Dispose();
        _cameraRegistration = null;
    }
}
