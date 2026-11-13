using System;
using System.Linq;
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
/// <summary>Camera start/stop: WPF MainWindow.SessionIO.cs:1274-1345 and MainWindow.BlinkTrainer.cs:310-373.</summary>
public partial class MainShellWindow
{
    private IDisposable? _cameraHotkeyRegistration;
    private (bool Global, ChordMods Mods, string Key)? _cameraHotkeyBinding;
    private PortalCameraHotkey? _portalCameraHotkey;
    private bool _cameraHotkeyBusy, _cameraKeyHooked;
    internal Func<Window, ChordMods, string, Action, IDisposable?> RegisterCameraHotkey = RegisterCameraHotkeyNative;
    internal Func<ChordMods, string, Action, PortalCameraHotkey> RegisterPortalCamera = (mods, key, pressed) =>
    {
        var portal = new PortalCameraHotkey(pressed);
        _ = portal.BindAsync(mods, key, Loc.Get("set2_palette_camera_shortcut"));
        return portal;
    };
    // WPF MainWindow.BlinkTrainer.cs:337-338: persisted consent alone does not finish the dialog.
    internal Func<Window, Task<bool>> ShowCameraConsent = async owner =>
    {
        var dialog = new WebcamConsentDialog();
        return await dialog.ShowDialogSafe<bool?>(owner) == true && dialog.ConsentGiven;
    };
    internal bool CameraHotkeyArmed => _cameraHotkeyRegistration != null || _portalCameraHotkey?.IsBound == true;
    private static IDisposable? RegisterCameraHotkeyNative(Window owner, ChordMods mods, string key, Action pressed)
        => OperatingSystem.IsWindows() ? Win32QuickRecalHotkey.RegisterCamera(owner, mods, key, pressed)
            : X11SummonChord.Register("camera", mods, key, pressed);
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
    internal static string CameraShortcutChord { get { var (m, k) = CameraChord(); return EmiDeskChord.Format(m, k); } }
    internal void ApplyCameraHotkey()
    {
        var (mods, key) = CameraChord();
        var binding = (CoreSettings.Current?.CompanionPrompt?.CameraShortcutGlobal != false, mods, key);
        if (_cameraHotkeyBinding == binding) return;
        _cameraHotkeyBinding = binding;
        _cameraHotkeyRegistration?.Dispose();
        _cameraHotkeyRegistration = null;
        _portalCameraHotkey?.Dispose();
        _portalCameraHotkey = null;
        if (_quickRecalClosed || !binding.Item1) return;
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            PortalCameraHotkey? portal = null;
            portal = RegisterPortalCamera(mods, key, () => Dispatcher.UIThread.Post(async () =>
            {
                var current = CameraChord();
                if (!ReferenceEquals(_portalCameraHotkey, portal) || portal?.IsBound != true
                    || CoreSettings.Current?.CompanionPrompt?.CameraShortcutGlobal == false
                    || current != (mods, key)) return;
                await ToggleWebcamFromHotkeyAsync();
            }));
            _portalCameraHotkey = portal;
            return;
        }
        try
        {
            _cameraHotkeyRegistration = RegisterCameraHotkey(this, mods, key,
                () => Dispatcher.UIThread.Post(async () => await ToggleWebcamFromHotkeyAsync()));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey registration failed"); }
    }
    /// <summary>WPF MainWindow.SessionIO.cs:1403 BtnCameraShortcut_Click: the chat capture dialog
    /// edits the camera combo, saves it, re-arms the hotkey and repaints both labels. One handler
    /// serves the Companion pill and the Settings Devices row, as in WPF.</summary>
    internal async Task RebindCameraShortcutAsync()
    {
        try
        {
            // Avalonia refuses a hidden owner; the shell can sit loaded-but-hidden in the tray.
            if (!IsVisible) return;
            var prompt = CoreSettings.Current?.CompanionPrompt;
            if (prompt == null) return;
            var dlg = new ChatShortcutCaptureDialog { GlobalHotkey = prompt.CameraShortcutGlobal };
            if (!await dlg.ShowDialogSafe<bool>(this)) return;
            if (dlg.ResetToDefault)
            {
                prompt.CameraShortcutKey = "K";
                prompt.CameraShortcutModifiers = "Control,Alt";
            }
            else
            {
                prompt.CameraShortcutKey = dlg.CapturedKey.ToString();
                // "Windows", not Avalonia's "Meta", so the file still parses on the WPF head.
                prompt.CameraShortcutModifiers = AvatarTube.AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            prompt.CameraShortcutGlobal = dlg.GlobalHotkey;
            CoreSettings.Save();
            // Only a desktop run that armed the chords re-arms them (StartQuickRecalHotkey); render/tests never grab keys.
            if (_quickRecalBindingWatch != null) ApplyCameraHotkey();
            RefreshCameraShortcutLabels();
        }
        catch (Exception ex) { Log.Warning(ex, "Camera shortcut rebind failed"); }
    }
    /// <summary>WPF MainWindow.SessionIO.cs:1381 RefreshCameraShortcutLabel: both pills read the saved combo.</summary>
    internal void RefreshCameraShortcutLabels()
    {
        foreach (var label in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<TextBlock>()
                     .Where(t => t.Name is "TxtCameraShortcutLabel" or "TxtCameraShortcutLabelDevices"))
            label.Text = CameraShortcutChord;
    }
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
                if (!await ShowCameraConsent(this)) return;
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
            }
            if (await tracker.StartAsync())
                App.Notifications.Show(Loc.Get("camera_shortcut_toast_started"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) { Log.Warning(ex, "Camera hotkey toggle failed"); }
        finally { _cameraHotkeyBusy = false; }
    }
}
