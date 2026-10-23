using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

/// <summary>The camera start/stop chord (default Ctrl+Alt+K): WPF ApplyGlobalCameraHotkey and
/// ApplyCameraShortcutTo (MainWindow.SessionIO.cs:1274-1345) plus ToggleWebcamFromHotkey /
/// ToggleWebcamTrackingAsync(announce: true) (MainWindow.BlinkTrainer.cs:310-373).</summary>
public partial class MainShellWindow
{
    private IDisposable? _cameraHotkeyRegistration;
    private (bool Global, ChordMods Mods, string Key)? _cameraHotkeyBinding;
    private bool _cameraHotkeyBusy, _cameraKeyHooked;

    // Native seam: headless tests never grab a key on the real desktop.
    internal Func<Window, ChordMods, string, Action, IDisposable?> RegisterCameraHotkey = RegisterCameraHotkeyNative;
    internal Func<Window, Task> ShowCameraConsent = owner => new WebcamConsentDialog().ShowDialogSafe(owner);
    internal bool CameraHotkeyArmed => _cameraHotkeyRegistration != null;

    private static IDisposable? RegisterCameraHotkeyNative(Window owner, ChordMods mods, string key, Action pressed)
        => OperatingSystem.IsWindows() ? Win32QuickRecalHotkey.RegisterCamera(owner, mods, key, pressed)
            : X11SummonChord.Register("camera", mods, key, pressed);

    /// <summary>WPF ParseModifiers/FormatCameraShortcut: unparseable key falls back to K, no modifier to Ctrl+Alt.</summary>
    internal static (ChordMods Mods, string Key) CameraChord()
    {
        var s = CoreSettings.Current?.CompanionPrompt;
        var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key) || key == Key.None) key = Key.K;
        var mods = ChordMods.None;
        foreach (var part in (s?.CameraShortcutModifiers ?? "Control,Alt").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            mods |= EmiDeskChord.Parse(part + "+X")?.Mods ?? ChordMods.None;
        return (mods == ChordMods.None ? ChordMods.Ctrl | ChordMods.Alt : mods, key.ToString());
    }

    /// <summary>"Ctrl+Alt+K" - WPF MainWindow.CameraShortcutChord.</summary>
    internal static string CameraShortcutChord { get { var (m, k) = CameraChord(); return EmiDeskChord.Format(m, k); } }

    /// <summary>WPF ApplyGlobalCameraHotkey: honours CameraShortcutGlobal; a refused combo leaves only the in-window key.</summary>
    internal void ApplyCameraHotkey()
    {
        var (mods, key) = CameraChord();
        var binding = (CoreSettings.Current?.CompanionPrompt?.CameraShortcutGlobal != false, mods, key);
        if (_cameraHotkeyBinding == binding) return;
        _cameraHotkeyBinding = binding;
        _cameraHotkeyRegistration?.Dispose();
        _cameraHotkeyRegistration = null;
        if (_quickRecalClosed || !binding.Item1) return;
        try
        {
            _cameraHotkeyRegistration = RegisterCameraHotkey(this, mods, key,
                () => Dispatcher.UIThread.Post(async () => await ToggleWebcamFromHotkeyAsync()));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey registration failed"); }
    }

    /// <summary>WPF ApplyCameraShortcutTo: the same chord works while this window has focus.</summary>
    private void HookCameraShortcutKey()
    {
        if (_cameraKeyHooked) return;
        _cameraKeyHooked = true;
        KeyDown += (_, e) =>
        {
            var (mods, key) = CameraChord();
            var m = ((e.KeyModifiers & KeyModifiers.Control) != 0 ? ChordMods.Ctrl : 0) | ((e.KeyModifiers & KeyModifiers.Alt) != 0 ? ChordMods.Alt : 0)
                  | ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? ChordMods.Shift : 0) | ((e.KeyModifiers & KeyModifiers.Meta) != 0 ? ChordMods.Win : 0);
            if (e.Handled || m != mods || e.Key.ToString() != key) return;
            e.Handled = true;
            _ = ToggleWebcamFromHotkeyAsync();
        };
    }

    /// <summary>WPF ToggleWebcamFromHotkey: stop if running, else consent (shell restored first so the
    /// modal is not lost behind a minimized owner) then start; a toast says which happened.</summary>
    internal async Task ToggleWebcamFromHotkeyAsync()
    {
        if (_cameraHotkeyBusy || _quickRecalClosed) return;
        _cameraHotkeyBusy = true;
        try
        {
            var tracker = WebcamTracker.Instance;
            if (tracker.IsRunning)
            {
                await tracker.StopAsync();
                App.Notifications.Show(Loc.Get("camera_shortcut_toast_stopped"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
                return;
            }
            if (!WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
                await ShowCameraConsent(this);
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
            }
            if (await tracker.StartAsync())
                App.Notifications.Show(Loc.Get("camera_shortcut_toast_started"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey toggle failed"); }
        finally { _cameraHotkeyBusy = false; }
    }
}
