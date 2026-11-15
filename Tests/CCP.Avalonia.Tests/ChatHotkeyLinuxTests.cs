using System;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChatHotkeyLinuxTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxRoutesSavedChordAndRebinds(bool portal) => AvaloniaTestDispatcher.Run(() =>
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal);
        var oldRoute = MainShellWindow.ChatUsesPortal;
        var oldX = MainShellWindow.RegisterChatX11;
        var oldPortal = MainShellWindow.RegisterChatPortal;
        var registrations = new System.Collections.Generic.List<Registration>();
        int calls = 0;
        IDisposable Register(ChordMods mods, string key)
        {
            calls++;
            Assert.Equal(calls == 1 ? ChordMods.Ctrl : ChordMods.Alt | ChordMods.Win, mods);
            Assert.Equal(calls == 1 ? "T" : "B", key);
            var r = new Registration(); registrations.Add(r); return r;
        }
        MainShellWindow.ChatUsesPortal = _ => portal;
        MainShellWindow.RegisterChatX11 = (id, mods, key, _) => { Assert.False(portal); Assert.Equal("chat", id); return Register(mods, key); };
        MainShellWindow.RegisterChatPortal = (mods, key, _) => { Assert.True(portal); return Register(mods, key); };
        var shell = new MainShellWindow();
        try
        {
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = ("T", "", true);
            shell.StartChatHotkey();
            Assert.Equal(1, calls);
            (s.ChatShortcutKey, s.ChatShortcutModifiers) = ("B", "Alt,Windows");
            shell.ApplyChatHotkey();
            Assert.True(registrations[0].Disposed);
            Assert.Equal(2, calls);
            s.ChatShortcutGlobal = false;
            shell.ApplyChatHotkey();
            Assert.True(registrations[1].Disposed);
            Assert.False(shell.ChatHotkeyArmed);
        }
        finally
        {
            shell.StopChatHotkey(); shell.Close();
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = old;
            MainShellWindow.ChatUsesPortal = oldRoute;
            MainShellWindow.RegisterChatX11 = oldX;
            MainShellWindow.RegisterChatPortal = oldPortal;
        }
    });

    [Fact]
    public void PortalRoutesOnlyItsSessionAndChatId()
    {
        int presses = 0;
        using var portal = new PortalChatHotkey(() => presses++);
        portal.SetSessionForTests("/session");
        portal.Route("/other", "chat", true);
        portal.Route("/session", "camera", true);
        portal.Route("/session", "chat", true);
        portal.Route("/session", "chat", true);
        Assert.Equal(1, presses);
        portal.Route("/session", "chat", false);
        portal.Route("/session", "chat", true);
        Assert.Equal(2, presses);
    }
}
