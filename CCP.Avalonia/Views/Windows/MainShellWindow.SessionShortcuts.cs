using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal static Func<Key, KeyModifiers, Action, IDisposable?> BindSessionHotkey = Platform.Win32SessionHotkey.Bind;
    private IDisposable? _chatHotkey, _cameraHotkey;
    private KeyBinding? _cameraBinding;
    private void InitializeSessionShortcuts()
    {
        Opened += (_, _) => ApplySessionShortcuts();
        Closed += (_, _) => { _chatHotkey?.Dispose(); _cameraHotkey?.Dispose(); };
    }
    internal static (Key Key, KeyModifiers Mods) SessionShortcut(bool camera)
    {
        var s = CoreSettings.Current.CompanionPrompt;
        var name = camera ? s?.CameraShortcutKey : s?.ChatShortcutKey;
        if (!Platform.HotkeyNames.TryParse(name, out var key)) key = camera ? Key.K : Key.T;
        var mods = KeyModifiers.None;
        foreach (var part in ((camera ? s?.CameraShortcutModifiers : s?.ChatShortcutModifiers) ?? "").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            mods |= part.ToLowerInvariant() switch { "control" => KeyModifiers.Control, "alt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift, "windows" => KeyModifiers.Meta, _ => KeyModifiers.None };
        if (mods == KeyModifiers.None) mods = camera ? KeyModifiers.Control | KeyModifiers.Alt : KeyModifiers.Control;
        return (key, mods);
    }
    internal static string CameraShortcutText()
    {
        var (key, mods) = SessionShortcut(true);
        var parts = new List<string>();
        if (mods.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
    internal void ApplySessionShortcuts()
    {
        _chatHotkey?.Dispose(); _chatHotkey = null;
        _cameraHotkey?.Dispose(); _cameraHotkey = null;
        AvatarTubeWindow.ApplyChatShortcutTo(this);
        AvatarTubeWindow.ApplyChatShortcutTo(AvatarTubeWindow.Live);
        if (_cameraBinding != null) KeyBindings.Remove(_cameraBinding);
        var camera = SessionShortcut(true);
        _cameraBinding = new KeyBinding { Gesture = new KeyGesture(camera.Key, camera.Mods), Command = new ShortcutAction(ToggleWebcamFromHotkey) };
        KeyBindings.Add(_cameraBinding);
        var s = CoreSettings.Current.CompanionPrompt;
        var chat = SessionShortcut(false);
        if (s?.ChatShortcutGlobal != false) _chatHotkey = BindSessionHotkey(chat.Key, chat.Mods, BringToForegroundAndOpenChat);
        if (s?.CameraShortcutGlobal != false) _cameraHotkey = BindSessionHotkey(camera.Key, camera.Mods, ToggleWebcamFromHotkey);
        foreach (var devices in this.GetVisualDescendants().OfType<DevicesSettingsSection>()) devices.RefreshSessionShortcutLabels();
        foreach (var behavior in this.GetVisualDescendants().OfType<WorkshopBehaviorCell>()) behavior.RefreshChatShortcutLabel();
    }
    private void BringToForegroundAndOpenChat()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); Topmost = true; Topmost = false;
        ShowAvatarTube();
        Tube?.OpenChatInput();
    }
    private async void ToggleWebcamFromHotkey()
    {
        if (!Platform.WebcamTracker.Instance.IsRunning && !Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
        { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); }
        if (Named<Tabs.BlinkTrainerTabView>("BlinkTrainerTab") is { } view) await view.ToggleTrackerAsync(announce: true);
    }
    internal async Task CaptureSessionShortcutAsync(bool camera)
    {
        try
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s == null) return;
            var dialog = new ChatShortcutCaptureDialog { GlobalHotkey = camera ? s.CameraShortcutGlobal : s.ChatShortcutGlobal };
            if (!await dialog.ShowDialogSafe<bool>(this)) return;
            var key = dialog.ResetToDefault ? (camera ? "K" : "T") : dialog.CapturedKey.ToString();
            var mods = dialog.ResetToDefault ? (camera ? "Control,Alt" : "Control") : AvatarTubeWindow.SerializeModifiers(dialog.CapturedModifiers);
            if (camera) { s.CameraShortcutKey = key; s.CameraShortcutModifiers = mods; s.CameraShortcutGlobal = dialog.GlobalHotkey; }
            else { s.ChatShortcutKey = key; s.ChatShortcutModifiers = mods; s.ChatShortcutGlobal = dialog.GlobalHotkey; }
            CoreSettings.Save();
            ApplySessionShortcuts();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Session shortcut capture failed"); }
    }
    private sealed class ShortcutAction(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
