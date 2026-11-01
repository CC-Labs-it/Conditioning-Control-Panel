using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private IDisposable? _cameraHotkey;
    private bool _cameraClosed, _cameraBusy, _cameraKeyHeld;
    private int _cameraGeneration;
    internal Func<Window, Key, KeyModifiers, Action, IDisposable?> RegisterCameraHotkey = RegisterCamera;
    internal Func<Task>? ToggleCameraForTest;

    // WPF MainWindow.SessionIO.cs:1274,1324,1360: the same defaults and modifier parsing
    // drive both the in-window fallback and the registered chord.
    internal static (Key Key, KeyModifiers Mods) CameraShortcut()
    {
        var s = CoreSettings.Current.CompanionPrompt;
        var key = HotkeyNames.TryParse(s?.CameraShortcutKey, out var parsed) ? parsed : Key.K;
        var mods = KeyModifiers.None;
        foreach (var part in (s?.CameraShortcutModifiers ?? "Control,Alt").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Shift;
            else if (part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) mods |= KeyModifiers.Meta;
        }
        if (mods == KeyModifiers.None) mods = KeyModifiers.Control | KeyModifiers.Alt;
        return (key, mods);
    }

    internal static string FormatCameraShortcut()
    {
        var (key, mods) = CameraShortcut();
        var parts = new List<string>();
        if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((mods & KeyModifiers.Meta) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private void InitializeCameraShortcut()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var (key, mods) = CameraShortcut();
            if (e.Key == key && e.KeyModifiers == mods && CameraKeySafe(key))
            {
                e.Handled = true;
                if (_cameraKeyHeld) return;
                _cameraKeyHeld = true;
                _ = ToggleCameraFromHotkeyAsync();
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == CameraShortcut().Key) _cameraKeyHeld = false; }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => _cameraKeyHeld = false;
    }

    internal void StartCameraHotkey()
    {
        ApplyCameraHotkey();
        Closed += (_, _) => StopCameraHotkey();
    }

    internal void ApplyCameraHotkey()
    {
        int generation = ++_cameraGeneration;
        _cameraHotkey?.Dispose();
        _cameraHotkey = null;
        var (key, mods) = CameraShortcut();
        if (_cameraClosed || CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal == false || !CameraKeySafe(key)) return;
        try { _cameraHotkey = RegisterCameraHotkey(this, key, mods, () => Dispatcher.UIThread.Post(() => { if (generation == _cameraGeneration) _ = ToggleCameraFromHotkeyAsync(); })); }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey registration failed"); }
    }

    private static bool CameraKeySafe(Key key) =>
        PanicPolicy.FindHookClash(key.ToString(), PanicPolicy.HookBoundBaseKeys(CoreSettings.Current)) == null;

    internal void StopCameraHotkey()
    {
        _cameraClosed = true;
        _cameraGeneration++;
        _cameraHotkey?.Dispose();
        _cameraHotkey = null;
    }

    private static IDisposable? RegisterCamera(Window owner, Key key, KeyModifiers mods, Action pressed)
    {
        if (OperatingSystem.IsWindows()) return Win32QuickRecalHotkey.RegisterCamera(owner, key, mods, pressed);
        var xmods = ((mods & KeyModifiers.Control) != 0 ? ChordMods.Ctrl : 0)
                  | ((mods & KeyModifiers.Alt) != 0 ? ChordMods.Alt : 0)
                  | ((mods & KeyModifiers.Shift) != 0 ? ChordMods.Shift : 0)
                  | ((mods & KeyModifiers.Meta) != 0 ? ChordMods.Win : 0);
        return X11SummonChord.Arm("camera", xmods, key.ToString(), pressed) ? new CameraGrab() : null;
    }

    private sealed class CameraGrab : IDisposable
    {
        public void Dispose() => X11SummonChord.Disarm("camera");
    }

    // WPF MainWindow.BlinkTrainer.cs:355: restore the owner only when consent is needed.
    internal async Task ToggleCameraFromHotkeyAsync()
    {
        if (_cameraClosed || _cameraBusy || !CameraKeySafe(CameraShortcut().Key)) return;
        _cameraBusy = true;
        try
        {
            if (ToggleCameraForTest != null) { await ToggleCameraForTest(); return; }
            if (!WebcamTracker.Instance.IsRunning && !WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
            }
            if (Named<Tabs.BlinkTrainerTabView>("BlinkTrainerTab") is { } page)
                await page.ToggleWebcamTrackingAsync(announce: true);
        }
        catch (Exception ex) { Log.Warning(ex, "Camera shortcut failed"); }
        finally { _cameraBusy = false; }
    }

    // WPF MainWindow.SessionIO.cs:1403: both settings doors use this one editor.
    internal async Task<bool> RebindCameraShortcutAsync()
    {
        if (!IsVisible) return false;
        var s = CoreSettings.Current.CompanionPrompt;
        if (s == null) return false;
        var dlg = new ChatShortcutCaptureDialog { GlobalHotkey = s.CameraShortcutGlobal };
        if (!await dlg.ShowDialogSafe<bool>(this)) return false;
        ApplyCapturedCameraShortcut(dlg.CapturedKey, dlg.CapturedModifiers, dlg.GlobalHotkey, dlg.ResetToDefault);
        return true;
    }

    internal void ApplyCapturedCameraShortcut(Key key, KeyModifiers mods, bool global, bool reset)
    {
        var s = CoreSettings.Current.CompanionPrompt;
        if (s == null) return;
        s.CameraShortcutKey = reset ? "K" : key.ToString();
        s.CameraShortcutModifiers = reset ? "Control,Alt" : AvatarTubeWindow.SerializeModifiers(mods);
        s.CameraShortcutGlobal = global;
        CoreSettings.Save();
        ApplyCameraHotkey();
        AppSettingsPage?.FindControl<Controls.AppSettings.DevicesSettingsSection>("SectionDevices")?.RefreshCameraShortcutLabel();
        foreach (var cell in this.GetVisualDescendants().OfType<Controls.Companion.Runtime.WorkshopBehaviorCell>())
            cell.RefreshCameraShortcutLabel();
    }
}
