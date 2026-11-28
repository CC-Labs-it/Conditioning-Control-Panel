using System;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using System.Collections.Generic;
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

    private sealed class RegistrationScope : IDisposable
    {
        private readonly EmiDeskService _svc;
        private readonly (bool Enabled, string Hotkey, bool Mute) _settings;
        private readonly Func<bool> _windows, _portal;
        private readonly Action _unregister;
        private readonly Func<ChordMods, string, Action, Action<bool>, IDisposable?> _register;

        internal RegistrationScope(EmiDeskService svc)
        {
            _svc = svc;
            var s = CoreSettings.Current;
            _settings = (s.EmiDeskEnabled, s.EmiDeskHotkey, s.EmiDeskMuteAvatar);
            _windows = svc.IsWindows; _portal = svc.UsesPortal;
            _unregister = svc.UnregisterChord; _register = svc.RegisterPortalChord;
        }

        public void Dispose()
        {
            var s = CoreSettings.Current;
            (s.EmiDeskEnabled, s.EmiDeskHotkey, s.EmiDeskMuteAvatar) = _settings;
            _svc.IsWindows = _windows; _svc.UsesPortal = _portal;
            _svc.UnregisterChord = _unregister; _svc.RegisterPortalChord = _register;
            _svc.ApplyHotkey();
        }
    }

    [Fact]
    public void PortalRoutesOnlyItsOwnSessionAndId()
    {
        int count = 0;
        var states = new List<bool>();
        using var portal = new PortalEmiHotkey(() => count++, states.Add);
        var mods = ChordMods.Ctrl | ChordMods.Alt;
        var id = PortalEmiHotkey.IdFor(mods, "E");
        Assert.NotEqual(id, PortalEmiHotkey.IdFor(mods, "B"));
        Assert.NotEqual(id, PortalEmiHotkey.IdFor(ChordMods.Ctrl, "E"));
        Assert.Equal(id, PortalEmiHotkey.IdFor(mods, "e"));
        portal.SetSessionForTests("/session", mods, "E");
        portal.Route("/other", id, true);
        portal.Route("/session", "chat", true);
        portal.Route("/session", id, true);
        portal.Route("/session", id, true);
        Assert.Equal(1, count);
        portal.Route("/session", id, false);
        portal.Route("/session", id, true);
        Assert.Equal(2, count);
        portal.Dispose();
        Assert.Equal(new[] { false }, states);
    }

    [Fact]
    public void PortalRebindsAndPanicCancelsQueuedSummon() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var svc = EmiDeskService.Instance;
        using var scope = new RegistrationScope(svc);
        svc.IsWindows = () => false;
        svc.UsesPortal = () => true;
        svc.UnregisterChord = () => { };
        Action? press = null;
        var first = new Registration();
        var second = new Registration();
        int calls = 0;
        svc.RegisterPortalChord = (mods, key, callback, changed) =>
        {
            Assert.Equal(ChordMods.Ctrl | ChordMods.Alt, mods);
            Assert.Equal(calls == 0 ? "E" : "B", key);
            press = callback;
            changed(true);
            return calls++ == 0 ? first : second;
        };
        try
        {
            settings.EmiDeskEnabled = true;
            settings.EmiDeskMuteAvatar = false;
            settings.EmiDeskHotkey = "Ctrl+Alt+E";
            svc.ApplyHotkey();
            Dispatcher.UIThread.RunJobs();
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

        }
    });
    [Fact]
    public void PortalStateUpdatesHintAndIgnoresOldSession() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var svc = EmiDeskService.Instance;
        using var scope = new RegistrationScope(svc);
        var callbacks = new List<Action<bool>>();
        svc.IsWindows = () => false;
        svc.UsesPortal = () => true;
        svc.UnregisterChord = () => { };
        svc.RegisterPortalChord = (_, _, _, changed) => { callbacks.Add(changed); return new Registration(); };
        var section = new EmiDeskSettingsSection();
        var host = new Window { Content = section };
        try
        {
            settings.EmiDeskEnabled = true;
            settings.EmiDeskHotkey = "Ctrl+Alt+E";
            host.Show();
            svc.ApplyHotkey();
            Assert.True(svc.HotkeyPending);
            Assert.False(svc.HotkeyArmed);
            var hint = section.FindControl<TextBlock>("TxtHotkeyHint")!;
            Assert.Equal(Loc.Get("set2_emi_desk_hotkey_hint"), hint.Text);
            callbacks[0](true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(svc.HotkeyArmed);
            settings.EmiDeskHotkey = "Ctrl+Alt+B";
            svc.ApplyHotkey();
            callbacks[0](true);
            callbacks[1](false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(svc.HotkeyPending);
            Assert.False(svc.HotkeyArmed);
            Assert.Equal(Loc.GetF("emi_desk_hotkey_err_portal", settings.EmiDeskHotkey), hint.Text);
            callbacks[1](true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(svc.HotkeyArmed);
            Assert.Equal(Loc.Get("set2_emi_desk_hotkey_hint"), hint.Text);
            callbacks[1](false); // portal closes after binding
            Dispatcher.UIThread.RunJobs();
            Assert.False(svc.HotkeyArmed);
            Assert.Equal(Loc.GetF("emi_desk_hotkey_err_portal", settings.EmiDeskHotkey), hint.Text);
            settings.EmiDeskHotkey = "";
            callbacks[1](false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.GetF("emi_desk_hotkey_err_portal", EmiDeskChord.DefaultHotkey), hint.Text);
        }
        finally
        {
            host.Close();

        }
    });

    [Fact]
    public void ScopeRestoresRegistrationAfterFailure() => AvaloniaTestDispatcher.Run(() =>
    {
        var svc = EmiDeskService.Instance;
        using var outer = new RegistrationScope(svc);
        var settings = CoreSettings.Current;
        settings.EmiDeskEnabled = true;
        settings.EmiDeskHotkey = "Ctrl+Alt+E";
        svc.IsWindows = () => false;
        svc.UsesPortal = () => false;
        svc.UnregisterChord = () => { };
        var previous = svc.RegisterChord;
        int arms = 0;
        svc.RegisterChord = (_, _, _) => { arms++; return true; };
        try
        {
            svc.ApplyHotkey();
            Assert.True(svc.HotkeyArmed);
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var scope = new RegistrationScope(svc);
                settings.EmiDeskEnabled = false;
                svc.ApplyHotkey();
                throw new InvalidOperationException();
            }));
            Assert.Equal(2, arms);
            Assert.True(svc.HotkeyArmed);
        }
        finally { svc.RegisterChord = previous; }
    });

}
