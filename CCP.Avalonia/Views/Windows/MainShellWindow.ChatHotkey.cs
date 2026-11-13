using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private IDisposable? _chatHotkey;
    private bool _chatHotkeyStarted, _chatHotkeyClosed;
    internal Func<Window, Key, KeyModifiers, Action, IDisposable?> RegisterChatHotkey = Win32ChatHotkey.Register;
    internal bool ChatHotkeyArmed => _chatHotkey != null;

    // WPF MainWindow.xaml.cs:237, once the native window exists.
    internal void StartChatHotkey()
    {
        if (_chatHotkeyStarted || _chatHotkeyClosed) return;
        _chatHotkeyStarted = true;
        Closed += (_, _) => StopChatHotkey();
        ApplyChatHotkey();
    }

    internal void ApplyChatHotkey()
    {
        // The local binding is also the fallback if native registration fails.
        AvatarTubeWindow.ApplyChatShortcutTo(this);
        _chatHotkey?.Dispose();
        _chatHotkey = null;
        if (!_chatHotkeyStarted || _chatHotkeyClosed || CoreSettings.Current.CompanionPrompt?.ChatShortcutGlobal == false) return;
        var (key, mods) = AvatarTubeWindow.CurrentChatShortcut();
        if (mods == KeyModifiers.None) mods = KeyModifiers.Control;
        try { _chatHotkey = RegisterChatHotkey(this, key, mods, () => Dispatcher.UIThread.Post(BringToForegroundAndOpenChat)); }
        catch (Exception ex) { Log.Warning(ex, "Chat shortcut registration failed"); }
    }

    internal void StopChatHotkey()
    {
        _chatHotkeyClosed = true;
        _chatHotkey?.Dispose();
        _chatHotkey = null;
    }

    // WPF MainWindow.SessionIO.cs:1202: restore, show the tube, and focus its chat.
    internal void BringToForegroundAndOpenChat()
    {
        if (_chatHotkeyClosed) return;
        try
        {
            RestoreLauncherPanelOpacity();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
            ShowAvatarTube();
            _avatarTubeWindow?.OpenChatInput();
        }
        catch (Exception ex) { Log.Warning(ex, "BringToForegroundAndOpenChat failed"); }
    }
}
