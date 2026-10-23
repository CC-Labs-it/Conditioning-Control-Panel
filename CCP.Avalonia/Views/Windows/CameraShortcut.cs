using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Webcam;
using Serilog;
using AvatarTubeWindow = ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The in-window camera shortcut (default Ctrl+Alt+K) and the shared tracker toggle behind it.
    /// Port of WPF ApplyCameraShortcutTo / FormatCameraShortcut / ToggleCameraCommand
    /// (MainWindow.SessionIO.cs:1264-1378) and ToggleWebcamFromHotkey / ToggleWebcamTrackingAsync
    /// (MainWindow.BlinkTrainer.cs:310-369). The system-wide half (ApplyGlobalCameraHotkey, a Win32
    /// RegisterHotKey) stays in the WPF head; the Global flag is still stored, as for chat.
    /// </summary>
    internal static class CameraShortcut
    {
        /// <summary>WPF ToggleCameraCommand. The KeyBinding passes its window as the parameter.</summary>
        internal static readonly ICommand ToggleCommand = new ToggleCameraCommand();

        private sealed class ToggleCameraCommand : ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter)
            {
                if (parameter is Window w) _ = ToggleFromHotkeyAsync(w);
            }
        }

        /// <summary>The stored combo, defaulted to Ctrl+Alt+K exactly as WPF reads it.</summary>
        internal static (Key Key, KeyModifiers Mods) Current()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
            var modsName = s?.CameraShortcutModifiers ?? "Control,Alt";
            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;
            // WPF ParseModifiers: an empty or unparseable set falls back to Ctrl+Alt.
            if (!AvatarTubeWindow.TryParseModifiers(modsName, out var mods) || mods == KeyModifiers.None)
                mods = KeyModifiers.Control | KeyModifiers.Alt;
            return (key, mods);
        }

        /// <summary>WPF FormatCameraShortcut: "Ctrl+Alt+K".</summary>
        internal static string Format()
        {
            var (key, mods) = Current();
            return AvatarTubeWindow.FormatChord(key, mods);
        }

        /// <summary>WPF ApplyCameraShortcutTo: replace this window's camera KeyBinding (never stacks).</summary>
        internal static void ApplyTo(Window window)
        {
            for (int i = window.KeyBindings.Count - 1; i >= 0; i--)
                if (ReferenceEquals(window.KeyBindings[i].Command, ToggleCommand))
                    window.KeyBindings.RemoveAt(i);

            var (key, mods) = Current();
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key, mods),
                Command = ToggleCommand,
                CommandParameter = window,
            });
        }

        /// <summary>
        /// WPF BtnCameraShortcut_Click (MainWindow.SessionIO.cs:1403): capture a combo, save it
        /// (Reset = Ctrl+Alt+K), re-apply the in-window binding. The caller repaints its label.
        /// </summary>
        internal static async Task<bool> RebindAsync(Window owner)
        {
            var prompt = CoreSettings.Current.CompanionPrompt;
            if (prompt == null) return false;

            var dlg = new Dialogs.ChatShortcutCaptureDialog { GlobalHotkey = prompt.CameraShortcutGlobal };
            if (!await dlg.ShowDialogSafe<bool>(owner)) return false;

            if (dlg.ResetToDefault)
            {
                prompt.CameraShortcutKey = "K";
                prompt.CameraShortcutModifiers = "Control,Alt";
            }
            else
            {
                prompt.CameraShortcutKey = dlg.CapturedKey.ToString();
                prompt.CameraShortcutModifiers = AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            prompt.CameraShortcutGlobal = dlg.GlobalHotkey;
            CoreSettings.Save();

            ApplyTo(owner);
            Log.Information("Camera shortcut rebound to {Combo}", Format());
            return true;
        }

        /// <summary>
        /// WPF ToggleWebcamFromHotkey + ToggleWebcamTrackingAsync(announce: true): a start that
        /// still needs consent restores the window first so the consent modal is not lost, and
        /// each toggle pops the short started/stopped toast.
        /// </summary>
        internal static async Task ToggleFromHotkeyAsync(Window owner)
        {
            try
            {
                if (!WebcamTracker.Instance.IsRunning && !WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                    owner.Show();
                    owner.Activate();
                }
                var result = await ToggleTrackerAsync(owner);
                if (result == false)
                    App.Notifications.Show(Loc.Get("camera_shortcut_toast_stopped"), NotificationType.Info, TimeSpan.FromSeconds(2));
                else if (result == true)
                    App.Notifications.Show(Loc.Get("camera_shortcut_toast_started"), NotificationType.Info, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { Log.Warning(ex, "Camera shortcut toggle failed"); }
        }

        /// <summary>
        /// The shared start/stop (WPF ToggleWebcamTrackingAsync, used by the Blink Trainer button and
        /// the shortcut): stop if running; else consent dialog when stale, then start. A failed start
        /// says why. Returns false when stopped, true when started, null when nothing changed.
        /// </summary>
        internal static async Task<bool?> ToggleTrackerAsync(Window owner, Action? beforeStart = null)
        {
            var tracker = WebcamTracker.Instance;
            if (tracker.IsRunning) { await tracker.StopAsync(); return false; }
            if (!WebcamConsent.IsCurrent(CoreSettings.Current))
            {
                await new Dialogs.WebcamConsentDialog().ShowDialogSafe(owner);
                if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return null;
            }
            beforeStart?.Invoke();
            if (await tracker.StartAsync()) return true;
            if (tracker.LastError != null)
                await Dialogs.MessageDialog.ShowAsync(owner, "Webcam tracking", tracker.LastError);
            return null;
        }
    }
}
