using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChatHotkeyTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private static void Init()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void StartupRebindDisableAndFailureKeepLocalFallback() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal);
        var shell = new MainShellWindow();
        var first = new Registration();
        var second = new Registration();
        int attempts = 0;
        try
        {
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = ("T", "Control", true);
            shell.RegisterChatHotkey = (_, key, mods, _) =>
            {
                attempts++;
                if (attempts == 1) { Assert.Equal(Key.T, key); Assert.Equal(KeyModifiers.Control, mods); return first; }
                Assert.Equal(Key.B, key); Assert.Equal(KeyModifiers.Alt | KeyModifiers.Meta, mods); return second;
            };
            shell.StartChatHotkey();
            shell.StartChatHotkey();
            Assert.Equal(1, attempts);
            Assert.True(shell.ChatHotkeyArmed);
            (s.ChatShortcutKey, s.ChatShortcutModifiers) = ("B", "Alt,Windows");
            shell.ApplyChatHotkey();
            Assert.True(first.Disposed);
            Assert.Equal(2, attempts);
            s.ChatShortcutGlobal = false;
            shell.ApplyChatHotkey();
            Assert.True(second.Disposed);
            Assert.False(shell.ChatHotkeyArmed);
            Assert.Single(shell.KeyBindings.Where(b => ReferenceEquals(b.Command, AvatarTubeWindow.OpenChatCommand)));
            s.ChatShortcutGlobal = true;
            shell.RegisterChatHotkey = (_, _, _, _) => null;
            shell.ApplyChatHotkey();
            Assert.False(shell.ChatHotkeyArmed);
            var fallback = Assert.Single(shell.KeyBindings.Where(b => ReferenceEquals(b.Command, AvatarTubeWindow.OpenChatCommand)));
            Assert.Equal(Key.B, fallback.Gesture!.Key);
            Assert.Equal(KeyModifiers.Alt | KeyModifiers.Meta, fallback.Gesture.KeyModifiers);
        }
        finally
        {
            shell.StopChatHotkey();
            shell.Close();
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = old;
        }
    });

    [Fact]
    public void HotkeyRestoresShellAndOpensChat() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var settings = CoreSettings.Current;
        bool old = settings.AvatarEnabled;
        var shell = new MainShellWindow();
        try
        {
            settings.AvatarEnabled = true;
            var tube = new AvatarTubeWindow(shell);
            typeof(MainShellWindow).GetField("_avatarTubeWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(shell, tube);
            shell.Show();
            tube.Show();
            var input = tube.FindControl<Border>("InputPanel")!;
            input.IsVisible = false;
            shell.WindowState = WindowState.Minimized;
            shell.Hide();
            shell.BringToForegroundAndOpenChat();
            Assert.True(shell.IsVisible);
            Assert.Equal(WindowState.Normal, shell.WindowState);
            Assert.True(tube.IsVisible);
            Assert.True(input.IsVisible);
        }
        finally { shell.Close(); settings.AvatarEnabled = old; }
    });
}
