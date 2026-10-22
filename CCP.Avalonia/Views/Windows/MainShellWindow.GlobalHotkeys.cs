// PORTED from ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs: ApplyGlobalChatHotkey (:1160),
// BringToForegroundAndOpenChat (:1197), ApplyGlobalCameraHotkey (:1324), ParseModifiers (:1346), and
// MainWindow.BlinkTrainer.cs ToggleWebcamFromHotkey (:350) with the hotkey half of ToggleWebcamTrackingAsync
// (:310, announce toasts). Armed at startup as WPF's shell Loaded does (MainWindow.xaml.cs:236-241), through
// Platform/X11SummonChord (the RegisterHotKey twin; X11/XWayland only, a Windows run of this head has no slot).
// ponytail: Quick Recal hotkey, the in-window camera KeyBinding and the camera rebind pills are
// wire-shell-app-startup-b-next (docs/avalonia-parity.md shell-app-startup).

using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The OS grab (WPF GlobalHotkeyService.Register/Unregister). Seams so tests never touch X.</summary>
        internal static Func<int, ChordMods, string, Action, bool> RegisterGlobalHotkey = X11SummonChord.Arm;
        internal static Action<int> UnregisterGlobalHotkey = X11SummonChord.Disarm;
        /// <summary>The shell a hotkey acts on. A seam: headless tests have no desktop lifetime.</summary>
        internal static Func<MainShellWindow?> HotkeyTarget = () => Current;

        /// <summary>App calls this once on the desktop path, after the panic key: the shell's in-window
        /// chat binding plus the two system-wide hotkeys (WPF MainWindow.xaml.cs:236-241).</summary>
        internal void StartGlobalHotkeys()
        {
            AvatarTubeWindow.ApplyChatShortcutTo(this);
            ApplyGlobalChatHotkey();
            ApplyGlobalCameraHotkey();
        }

        /// <summary>WPF ApplyGlobalChatHotkey: honour the Global toggle, else arm the saved combo (default
        /// Ctrl+T). A refused combo leaves the in-window binding as the fallback.</summary>
        internal static void ApplyGlobalChatHotkey()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s?.ChatShortcutGlobal == false) { UnregisterGlobalHotkey(X11SummonChord.ChatId); return; }
            var (mods, key) = ParseShortcut(s?.ChatShortcutKey, s?.ChatShortcutModifiers ?? "Control", "T", ChordMods.Ctrl);
            // X delivers the press on the listener thread: marshal to the UI thread.
            RegisterGlobalHotkey(X11SummonChord.ChatId, mods, key,
                () => Dispatcher.UIThread.Post(() => HotkeyTarget()?.BringToForegroundAndOpenChat()));
        }

        /// <summary>WPF ApplyGlobalCameraHotkey: same shape, default Ctrl+Alt+K, toggles the tracker.</summary>
        internal static void ApplyGlobalCameraHotkey()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s?.CameraShortcutGlobal == false) { UnregisterGlobalHotkey(X11SummonChord.CameraId); return; }
            var (mods, key) = ParseShortcut(s?.CameraShortcutKey, s?.CameraShortcutModifiers ?? "Control,Alt", "K", ChordMods.Ctrl | ChordMods.Alt);
            RegisterGlobalHotkey(X11SummonChord.CameraId, mods, key,
                () => Dispatcher.UIThread.Post(() => HotkeyTarget()?.ToggleWebcamFromHotkey()));
        }

        /// <summary>The stored "T" / "Control,Alt" pair as a chord: an unknown key falls back to the default
        /// key, no recognised modifier to the default modifiers (WPF ParseModifiers, Enum.TryParse&lt;Key&gt;).
        /// "Windows" is what both heads store for the Win key.</summary>
        internal static (ChordMods Mods, string Key) ParseShortcut(string? keyName, string? modsName, string defaultKey, ChordMods defaultMods)
        {
            if (string.IsNullOrWhiteSpace(keyName)
                || !Enum.TryParse<global::Avalonia.Input.Key>(keyName, ignoreCase: true, out var key))
                key = Enum.Parse<global::Avalonia.Input.Key>(defaultKey);
            var mods = ChordMods.None;
            foreach (var part in (modsName ?? "").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                mods |= part.ToLowerInvariant() switch
                {
                    "control" or "ctrl" => ChordMods.Ctrl,
                    "alt" => ChordMods.Alt,
                    "shift" => ChordMods.Shift,
                    "windows" or "win" or "meta" => ChordMods.Win,
                    _ => ChordMods.None,
                };
            return (mods == ChordMods.None ? defaultMods : mods, key.ToString());
        }

        /// <summary>WPF BringToForegroundAndOpenChat: restore and raise the shell, re-show the tube (it hides
        /// while the shell is minimized), then open the chat input.</summary>
        internal void BringToForegroundAndOpenChat()
        {
            try
            {
                RaiseForHotkey();
                ShowAvatarTube();
                AvatarTubeWindow.Live?.OpenChatInput();
            }
            catch (Exception ex) { Log.Warning(ex, "BringToForegroundAndOpenChat failed"); }
        }

        private void RaiseForHotkey()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
        }

        /// <summary>WPF ToggleWebcamFromHotkey + ToggleWebcamTrackingAsync(announce: true): stop when running;
        /// else the consent dialog when stale (shell raised first so the modal is not lost behind the tray),
        /// then start. Each toggle says so in a short toast; a failed start stays silent as in WPF.</summary>
        internal async void ToggleWebcamFromHotkey()
        {
            try
            {
                var tracker = WebcamTracker.Instance;
                if (tracker.IsRunning)
                {
                    await tracker.StopAsync();
                    App.Notifications.Show(Loc.Get("camera_shortcut_toast_stopped"), NotificationType.Info, TimeSpan.FromSeconds(2));
                    return;
                }
                if (!WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    RaiseForHotkey();
                    await new Dialogs.WebcamConsentDialog().ShowDialogSafe(this);
                    if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                if (await tracker.StartAsync())
                    App.Notifications.Show(Loc.Get("camera_shortcut_toast_started"), NotificationType.Info, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { Log.Warning(ex, "ToggleWebcamFromHotkey failed"); }
        }
    }
}
