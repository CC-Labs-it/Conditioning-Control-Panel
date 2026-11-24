using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Serilog;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private IDisposable? _chatHotkey;
    private bool _chatHotkeyStarted, _chatHotkeyClosed;
    internal Func<Window, Key, KeyModifiers, Action, IDisposable?> RegisterChatHotkey = RegisterChatHotkeyNative;
    internal static Func<Window, bool> ChatUsesPortal = owner =>
        owner.TryGetPlatformHandle()?.HandleDescriptor != "XID" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    internal static Func<string, ChordMods, string, Action, IDisposable?> RegisterChatX11 = X11SummonChord.Register;
    internal static Func<ChordMods, string, Action, IDisposable?> RegisterChatPortal = (mods, key, pressed) =>
    {
        var portal = new PortalChatHotkey(pressed);
        _ = portal.BindAsync(mods, key, Loc.Get("set2_palette_chat_shortcut"));
        return portal;
    };
    private static IDisposable? RegisterChatHotkeyNative(Window owner, Key key, KeyModifiers mods, Action pressed)
    {
        if (OperatingSystem.IsWindows()) return Win32ChatHotkey.Register(owner, key, mods, pressed);
        if (!OperatingSystem.IsLinux()) return null;
        var chord = ((mods & KeyModifiers.Control) != 0 ? ChordMods.Ctrl : 0)
            | ((mods & KeyModifiers.Alt) != 0 ? ChordMods.Alt : 0)
            | ((mods & KeyModifiers.Shift) != 0 ? ChordMods.Shift : 0)
            | ((mods & KeyModifiers.Meta) != 0 ? ChordMods.Win : 0);
        return ChatUsesPortal(owner) ? RegisterChatPortal(chord, key.ToString(), pressed)
            : RegisterChatX11("chat", chord, key.ToString(), pressed);
    }
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
