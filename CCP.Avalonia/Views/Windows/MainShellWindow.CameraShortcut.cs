using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private readonly X11Chord _cameraChord = new();
    private readonly Win32CameraChord _windowsCameraChord = new();
    private void DisarmCameraChord() { _cameraChord.Disarm(); _windowsCameraChord.Disarm(); }
    // A CCP_USERDATA_DIR sandbox (tests, kc, render-all) never grabs a key on the real desktop.
    private bool ArmCameraChord(ChordMods mods, string key, Action callback) => !SandboxNet.Active && (OperatingSystem.IsWindows()
        ? _windowsCameraChord.Arm(mods, key, callback) : _cameraChord.Arm(mods, key, callback));
    private bool _cameraChordArmed;
    private bool _cameraToggleBusy;
    private bool _cameraShortcutClosed;
    internal Func<ChordMods, string, Action, bool>? RegisterCameraChord;
    internal Action? UnregisterCameraChord;

    private void InitializeCameraShortcut()
    {
        Opened += (_, _) =>
        {
            if (CoreSettings.Service is { } settings)
            { settings.CurrentReplaced -= OnCameraSettingsReplaced; settings.CurrentReplaced += OnCameraSettingsReplaced; }
            ApplyCameraShortcut();
        };
        Closed += (_, _) => { if (CoreSettings.Service is { } settings) settings.CurrentReplaced -= OnCameraSettingsReplaced; _cameraShortcutClosed = true; (UnregisterCameraChord ?? DisarmCameraChord)(); _cameraChordArmed = false; };
        KeyDown += (_, e) =>
        {
            var (key, mods) = CameraShortcut();
            if (!_cameraChordArmed && !e.Handled && e.Key == key && e.KeyModifiers == mods)
            { e.Handled = true; _ = ToggleWebcamFromHotkey(); }
        };
    }

    private void OnCameraSettingsReplaced() => Dispatcher.UIThread.Post(() => { if (!_cameraShortcutClosed) ApplyCameraShortcut(); });

    internal async Task RebindCameraShortcut()
    {
        var prompt = CoreSettings.Current.CompanionPrompt;
        if (prompt == null) return;
        var dlg = new ChatShortcutCaptureDialog { GlobalHotkey = prompt.CameraShortcutGlobal };
        if (!await dlg.ShowDialogSafe<bool>(this)) return;
        prompt.CameraShortcutKey = dlg.ResetToDefault ? "K" : dlg.CapturedKey.ToString();
        prompt.CameraShortcutModifiers = dlg.ResetToDefault ? "Control,Alt" : AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
        prompt.CameraShortcutGlobal = dlg.GlobalHotkey;
        CoreSettings.Save();
        ApplyCameraShortcut();
        foreach (var label in this.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name is "TxtCameraShortcutLabel" or "TxtCameraShortcutLabelDevices"))
            label.Text = FormatCameraShortcut();
    }

    internal static (Key Key, KeyModifiers Mods) CameraShortcut()
    {
        var s = CoreSettings.Current.CompanionPrompt;
        if (!Enum.TryParse<Key>(s?.CameraShortcutKey, true, out var key) || key == Key.None) key = Key.K;
        var mods = KeyModifiers.None;
        foreach (var part in (s?.CameraShortcutModifiers ?? "Control,Alt").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            mods |= part.ToLowerInvariant() switch
            {
                "control" or "ctrl" => KeyModifiers.Control, "alt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift, "windows" or "win" or "meta" => KeyModifiers.Meta, _ => KeyModifiers.None
            };
        if (mods == KeyModifiers.None) mods = KeyModifiers.Control | KeyModifiers.Alt;
        return (key, mods);
    }

    internal static string FormatCameraShortcut()
    {
        var (key, mods) = CameraShortcut();
        return string.Join("+", AvatarTubeWindow.SerializeModifiers(mods).Split(',').Select(x => x.Trim() switch
        { "Control" => "Ctrl", "Windows" => "Win", var x2 => x2 }).Append(key.ToString()));
    }

    internal void ApplyCameraShortcut()
    {
        (UnregisterCameraChord ?? DisarmCameraChord)();
        _cameraChordArmed = false;
        if (CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal == false) return;
        var (key, mods) = CameraShortcut();
        var chordMods = ChordMods.None;
        if (mods.HasFlag(KeyModifiers.Control)) chordMods |= ChordMods.Ctrl;
        if (mods.HasFlag(KeyModifiers.Alt)) chordMods |= ChordMods.Alt;
        if (mods.HasFlag(KeyModifiers.Shift)) chordMods |= ChordMods.Shift;
        if (mods.HasFlag(KeyModifiers.Meta)) chordMods |= ChordMods.Win;
        // ponytail: native Wayland needs a separate portal chord; retain the in-window fallback.
        _cameraChordArmed = (RegisterCameraChord ?? ArmCameraChord)(chordMods, key.ToString(),
            () => Dispatcher.UIThread.Post(() => { if (!_cameraShortcutClosed) _ = ToggleWebcamFromHotkey(); }));
    }

    internal async Task ToggleWebcamFromHotkey()
    {
        if (_cameraToggleBusy) return;
        _cameraToggleBusy = true;
        try
        {
            if (!WebcamTracker.Instance.IsRunning && !WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
            }
            // Named<T>: x:Name fields are null on this window (CLAUDE.md trap 7).
            if (Named<Tabs.BlinkTrainerTabView>("BlinkTrainerTab") is { } tab)
                await tab.ToggleWebcamTrackingAsync(announce: true);
        }
        finally { _cameraToggleBusy = false; }
    }
}
