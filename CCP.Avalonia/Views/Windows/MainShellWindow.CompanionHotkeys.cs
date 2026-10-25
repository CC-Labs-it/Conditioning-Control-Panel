using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private Win32CompanionHotkeys? _companionHotkeys;

    private void InitializeCompanionHotkeys()
    {
        Opened += (_, _) =>
        {
            _companionHotkeys ??= Win32CompanionHotkeys.Attach(this);
            RefreshCompanionHotkeys();
        };
        if (CoreSettings.Service is { } settings)
        {
            void Replaced() => Dispatcher.UIThread.Post(() =>
            {
                if (_windowClosed) return;
                AvatarTubeWindow.ApplyChatShortcutTo(this);
                CameraShortcut.ApplyTo(this);
                RefreshCompanionHotkeys();
            });
            settings.CurrentReplaced += Replaced;
            Closed += (_, _) => settings.CurrentReplaced -= Replaced;
        }
        Closed += (_, _) => { _companionHotkeys?.Dispose(); _companionHotkeys = null; };
    }

    internal void RefreshCompanionHotkeys()
    {
        if (_companionHotkeys == null) return;
        var prompt = CoreSettings.Current.CompanionPrompt;
        var (key, mods) = AvatarTubeWindow.CurrentChatShortcut();
        // WPF global parser falls back to Control for an empty modifier list.
        if (mods == KeyModifiers.None) mods = KeyModifiers.Control;
        _companionHotkeys.Set(Win32CompanionHotkeys.Chat, prompt?.ChatShortcutGlobal != false, key, mods,
            BringToForegroundAndOpenChat);
        (key, mods) = CameraShortcut.Current();
        _companionHotkeys.Set(Win32CompanionHotkeys.Camera, prompt?.CameraShortcutGlobal != false, key, mods,
            () => _ = CameraShortcut.ToggleFromHotkeyAsync(this));
    }

    internal void BringToForegroundAndOpenChat()
    {
        if (_windowClosed) return;
        try   // WPF SessionIO.cs:1204-1228: a hotkey callback must never throw into the message loop
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate(); Topmost = true; Topmost = false;
            ShowAvatarTube();
            Tube?.OpenChatInput();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Companion chat hotkey: bring to foreground failed"); }
    }
}
