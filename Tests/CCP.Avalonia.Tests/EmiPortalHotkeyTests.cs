using System;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiPortalHotkeyTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void PortalRoutesOnlyItsOwnSessionAndId()
    {
        int count = 0;
        using var portal = new PortalEmiHotkey(() => count++);
        portal.SetSessionForTests("/session");
        portal.Route("/other", "emi-desk", true);
        portal.Route("/session", "chat", true);
        portal.Route("/session", "emi-desk", true);
        portal.Route("/session", "emi-desk", true);
        Assert.Equal(1, count);
        portal.Route("/session", "emi-desk", false);
        portal.Route("/session", "emi-desk", true);
        Assert.Equal(2, count);
    }

    [Fact]
    public void PortalRebindsAndPanicCancelsQueuedSummon() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = (settings.EmiDeskEnabled, settings.EmiDeskHotkey, settings.EmiDeskMuteAvatar);
        var svc = EmiDeskService.Instance;
        var oldWindows = svc.IsWindows;
        var oldUsesPortal = svc.UsesPortal;
        var oldUnregister = svc.UnregisterChord;
        var oldRegister = svc.RegisterPortalChord;
        svc.IsWindows = () => false;
        svc.UsesPortal = () => true;
        svc.UnregisterChord = () => { };
        Action? press = null;
        var first = new Registration();
        var second = new Registration();
        int calls = 0;
        svc.RegisterPortalChord = (mods, key, callback) =>
        {
            Assert.Equal(ChordMods.Ctrl | ChordMods.Alt, mods);
            Assert.Equal(calls == 0 ? "E" : "B", key);
            press = callback;
            return calls++ == 0 ? first : second;
        };
        try
        {
            settings.EmiDeskEnabled = true;
            settings.EmiDeskMuteAvatar = false;
            settings.EmiDeskHotkey = "Ctrl+Alt+E";
            svc.ApplyHotkey();
            Assert.True(svc.HotkeyArmed);
            settings.EmiDeskHotkey = "Ctrl+Alt+B";
            svc.ApplyHotkey();
            Assert.True(first.Disposed);
            Assert.Equal(2, calls);
            press!();
            PanicSurfaces.All.Single(s => s.Id == "emi-desk-feedback").Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(svc.IsOut);
            press!();
            Dispatcher.UIThread.RunJobs();
            Assert.True(svc.IsOut);
            svc.Dismiss();
            settings.EmiDeskEnabled = false;
            svc.ApplyHotkey();
            Assert.True(second.Disposed);
            Assert.False(svc.HotkeyArmed);
        }
        finally
        {
            if (svc.IsOut) svc.Dismiss();
            (settings.EmiDeskEnabled, settings.EmiDeskHotkey, settings.EmiDeskMuteAvatar) = old;
            svc.IsWindows = oldWindows;
            svc.UsesPortal = oldUsesPortal;
            svc.UnregisterChord = oldUnregister;
            svc.RegisterPortalChord = oldRegister;
        }
    });
}
