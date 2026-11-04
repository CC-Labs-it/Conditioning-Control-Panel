using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
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
        var avatarEnabled = CoreSettings.Current.AvatarEnabled;
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
            CoreSettings.Current.AvatarEnabled = false;
            shell.Show();  // the same Opened event wired by the shell constructor
            shell.Hide();
            shell.Show();  // tray reopen must not register a second time
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
            CoreSettings.Current.AvatarEnabled = avatarEnabled;
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
            var text = tube.FindControl<TextBox>("TxtUserInput")!;
            text.Text = "hello";
            int foregroundCalls = 0;
            tube.FocusChatWindow = window => { Assert.Same(tube, window); Assert.True(input.IsVisible); foregroundCalls++; };
            var topmost = new System.Collections.Generic.List<bool>();
            shell.PropertyChanged += (_, e) => { if (e.Property == Window.TopmostProperty) topmost.Add(shell.Topmost); };
            input.IsVisible = false;
            shell.WindowState = WindowState.Minimized;
            shell.Hide();
            shell.BringToForegroundAndOpenChat();
            Assert.True(shell.IsVisible);
            Assert.Equal(WindowState.Normal, shell.WindowState);
            Assert.True(tube.IsVisible);
            Assert.True(input.IsVisible);
            Assert.Equal(new[] { true, false }, topmost);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, foregroundCalls);
            Assert.Equal(0, text.SelectionStart);
            Assert.Equal(text.Text!.Length, text.SelectionEnd);
        }
        finally { shell.Close(); settings.AvatarEnabled = old; }
    });
    [Fact]
    public void ChatInputRaisesWindowBeforeFocusingAndSelectingText() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            var text = tube.FindControl<TextBox>("TxtUserInput")!;
            text.Text = "hello";
            int calls = 0;
            tube.FocusChatWindow = window => { Assert.Same(tube, window); calls++; window.Activate(); };
            tube.OpenChatInput();
            Assert.Equal(0, calls); // queued until the panel is laid out
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calls);
            Assert.True(text.IsFocused);
            Assert.Equal(0, text.SelectionStart);
            Assert.Equal(5, text.SelectionEnd);
        }
        finally { tube.Close(); }
    });

    [Fact]
    public void HotkeyPreservesMaximizedState() => AvaloniaTestDispatcher.Run(() =>
    {
        Init();
        var settings = CoreSettings.Current;
        bool old = settings.AvatarEnabled;
        var shell = new MainShellWindow();
        try
        {
            settings.AvatarEnabled = false;
            shell.Show();
            shell.WindowState = WindowState.Maximized;
            shell.BringToForegroundAndOpenChat();
            Assert.Equal(WindowState.Maximized, shell.WindowState);
        }
        finally { shell.Close(); settings.AvatarEnabled = old; }
    });

}
