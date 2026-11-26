using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>restack b40 (#3866): WPF's WH_KEYBOARD_LL hook hears any bound key and reads the setting per press
/// (MainWindow.xaml.cs:943-960; GlobalKeyboardHook.cs:165). On Linux a key X cannot hear is warned about once, and
/// a rebind while the GlobalShortcuts portal holds the old key re-binds the portal. The one-press rules of #4442
/// (PanicRouteTests) must survive the re-bind: one press heard on both routes counts once, a held key is one press,
/// and a missing or refusing portal leaves XInput2 panicking on its own.</summary>
public sealed class PanicKeyPortalRebindTests
{
    [Fact]
    public void AKeyXCannotResolve_IsRaisedToTheUser_NeverOnlyLogged()
    {
        var old = X11PanicKey.Unhearable;
        var heard = new List<string?>();
        try
        {
            X11PanicKey.Unhearable = heard.Add;
            Assert.Equal(9, X11PanicKey.ResolveKeycode("Escape", _ => 9));
            Assert.Empty(heard);
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Equal(new string?[] { "Oem8" }, heard);
            // A keymap change (MappingNotify) re-resolves the same dead key: no second toast.
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Single(heard);
            // A different dead key is news; a key that fires and then dies again is news too.
            Assert.Equal(0, X11PanicKey.ResolveKeycode("FnLeftArrow", _ => 0));
            Assert.Equal(9, X11PanicKey.ResolveKeycode("Escape", _ => 9));
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Equal(new string?[] { "Oem8", "FnLeftArrow", "Oem8" }, heard);
            // A throwing handler never reaches the listener loop.
            X11PanicKey.Unhearable = _ => throw new InvalidOperationException("toast failed");
            Assert.Equal(0, X11PanicKey.ResolveKeycode("F13", _ => 0));
        }
        finally { X11PanicKey.ResolveKeycode("Escape", _ => 9); X11PanicKey.Unhearable = old; }
    }

    [Fact]
    public void PortalPresent_ARebindRebindsAfterTheCapture_OnePressStillCountsOnce_AHeldKeyIsOnePress() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        var keys = new List<string?>();
        Action? down = null;
        PortalPanicShortcut.BindForTests = (d, up) =>
        {
            down = d;
            keys.Add(MainShellWindow.PortalKeyForTest);
            PortalPanicShortcut.SetSessionForTests("/test/rebind-" + keys.Count, up);
            return Task.FromResult<string?>(MainShellWindow.PortalKeyForTest);
        };
        CoreFlash.Start();   // an effect keeps the grab wanted
        MainShellWindow.StartEffect(() => { });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "Escape" }, keys);
        Assert.True(PortalPanicShortcut.IsBound);

        // Mid-capture nothing moves; once the capture is over the old grab closes and the new key binds.
        MainShellWindow.CapturingPanicKey = true;
        s.PanicKey = "F8";
        MainShellWindow.ReleasePortalWhenUnused(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "Escape" }, keys);
        MainShellWindow.CapturingPanicKey = false;
        MainShellWindow.ReleasePortalWhenUnused(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "Escape", "F8" }, keys);
        Assert.True(PortalPanicShortcut.IsBound);
        MainShellWindow.ReleasePortalWhenUnused(shell);   // same key now: no second round trip
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, keys.Count);

        // A held key on the re-bound session is one press: the compositor's repeats are not new presses.
        int downs = 0;
        var t0 = DateTime.UtcNow;
        PortalPanicShortcut.Route("/test/rebind-2", "panic", true, () => downs++, () => { }, t0);
        PortalPanicShortcut.Route("/test/rebind-2", "panic", true, () => downs++, () => { }, t0.AddMilliseconds(30));
        PortalPanicShortcut.Route("/test/rebind-2", "panic", true, () => downs++, () => { }, t0.AddMilliseconds(60));
        Assert.Equal(1, downs);
        Assert.False(PortalPanicShortcut.Route("/test/rebind-1", "panic", true, () => downs++, () => { }, t0));   // the old session is gone
        PortalPanicShortcut.Route("/test/rebind-2", "panic", false, () => { }, () => { }, t0.AddMilliseconds(90));

        // One physical press heard by XInput2 and the re-bound portal is still ONE panic press (#4442).
        setClock(5000);
        shell.OnX11PanicPress();
        shell.OnPortalPanicActivated();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
    });

    [Fact]
    public void PortalMissing_NoRebindIsTried_AndXInput2AlonePanics() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        int binds = 0;
        PortalPanicShortcut.BindForTests = (_, _) => { binds++; return Task.FromResult<string?>(null); };   // absent or refused
        CoreFlash.Start();
        bool started = false;
        MainShellWindow.StartEffect(() => started = true);
        for (var i = 0; i < 200 && !started; i++) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(10); }   // the start rides a pool continuation
        Assert.True(started);   // the effect starts anyway
        Assert.Equal(1, binds);
        Assert.False(PortalPanicShortcut.IsBound);

        s.PanicKey = "F8";
        MainShellWindow.ReleasePortalWhenUnused(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, binds);   // a refused portal is not asked again for a rebind

        setClock(5000);
        shell.OnX11PanicPress();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
        Assert.False(CoreFlash.IsRunning);   // the press stopped the effect
    });

    // ---- ccp-bugs #1752: after a rebind from Escape to NumLock, BOTH keys panicked on Wayland. The desktop keeps
    // the trigger it first gave the one shortcut id "panic", so each key now binds its own id, a report for a key
    // that is no longer the setting is dropped, and a desktop that binds another key has its grab released.

    [Fact]
    public void EachKeyHasItsOwnShortcutId_AndTheSameKeyKeepsItsId()
    {
        Assert.Equal("panic-escape", PortalPanicShortcut.IdFor("Escape"));
        Assert.Equal("panic-numlock", PortalPanicShortcut.IdFor("NumLock"));
        Assert.Equal(PortalPanicShortcut.IdFor("F8"), PortalPanicShortcut.IdFor("F8"));
        Assert.NotEqual(PortalPanicShortcut.IdFor("Escape"), PortalPanicShortcut.IdFor("NumLock"));
        Assert.NotEqual(PortalPanicShortcut.Id, PortalPanicShortcut.IdFor("Escape"));   // never the old shared id
        Assert.Equal("panic-none", PortalPanicShortcut.IdFor(""));
        Assert.Matches("^panic-[a-z0-9]+$", PortalPanicShortcut.IdFor("Oem102"));
    }

    [Theory]
    [InlineData("NumLock", "Num Lock", "Chosen")]     // KDE
    [InlineData("NumLock", "Num_Lock", "Chosen")]     // keysym
    [InlineData("Escape", "Esc", "Chosen")]
    [InlineData("Escape", "Escape", "Chosen")]
    [InlineData("PageUp", "Prior", "Chosen")]
    [InlineData("Scroll", "Scroll Lock", "Chosen")]
    [InlineData("D1", "1", "Chosen")]
    [InlineData("F8", "F8", "Chosen")]
    [InlineData("NumLock", "Escape", "Other")]        // the #1752 report
    [InlineData("NumLock", "Esc", "Other")]
    [InlineData("NumLock", "Esc, Num Lock", "Other")] // the old key rides along
    [InlineData("NumLock", "Ctrl+Num Lock", "Other")]
    [InlineData("Escape", "<Control>Escape", "Other")]
    [InlineData("F8", "F9", "Other")]
    [InlineData("NumLock", "", "Unknown")]
    [InlineData("NumLock", "\u21ed", "Unknown")]       // a glyph we cannot read
    [InlineData("Delete", "Entf", "Unknown")]          // a German label
    public void TheReportedTriggerIsReadAsTheChosenKey_AnotherKey_OrUnknown(string key, string trigger, string want) =>
        Assert.Equal(Enum.Parse<PortalPanicShortcut.TriggerMatch>(want), PortalPanicShortcut.MatchTrigger(key, trigger));

    [Fact]
    public void RebindTwice_EachSessionAnswersOnlyItsOwnKey_AndTheOldKeysNeverPanic() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        var keys = new List<string>();
        BindLikeTheDesktop(keys, key => key);
        CoreFlash.Start();
        MainShellWindow.StartEffect(() => { });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PortalPanicShortcut.IdFor("Escape"), PortalPanicShortcut.BoundId);

        s.PanicKey = "NumLock";
        MainShellWindow.ReleasePortalWhenUnused(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Escape", "NumLock" }, keys);
        Assert.Equal(PortalPanicShortcut.IdFor("NumLock"), PortalPanicShortcut.BoundId);
        var t0 = DateTime.UtcNow;
        int heard = 0;
        Assert.False(PortalPanicShortcut.Route("/test/id-2", PortalPanicShortcut.IdFor("Escape"), true, () => heard++, () => { }, t0));
        Assert.False(PortalPanicShortcut.Route("/test/id-2", PortalPanicShortcut.Id, true, () => heard++, () => { }, t0));
        Assert.True(PortalPanicShortcut.Route("/test/id-2", PortalPanicShortcut.IdFor("NumLock"), true, () => heard++, () => { }, t0));
        PortalPanicShortcut.Route("/test/id-2", PortalPanicShortcut.IdFor("NumLock"), false, () => { }, () => { }, t0.AddMilliseconds(50));
        Assert.Equal(1, heard);

        s.PanicKey = "F8";
        MainShellWindow.ReleasePortalWhenUnused(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Escape", "NumLock", "F8" }, keys);
        Assert.Equal(PortalPanicShortcut.IdFor("F8"), PortalPanicShortcut.BoundId);
        Assert.False(PortalPanicShortcut.Route("/test/id-3", PortalPanicShortcut.IdFor("NumLock"), true, () => heard++, () => { }, t0.AddSeconds(1)));
        Assert.Equal(1, heard);

        // A late report from either earlier session (its callback) is not a panic; the current key is.
        setClock(5000);
        MainShellWindow.PortalActivated(keys[0], shell);
        MainShellWindow.PortalActivated(keys[1], shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, shell.PanicPressCountForTest);
        Assert.True(CoreFlash.IsRunning);
        MainShellWindow.PortalActivated(keys[2], shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
        Assert.False(CoreFlash.IsRunning);
    });

    [Fact]
    public void AReplacedKeyIsNeverAPanic_EvenBeforeTheWatchRebinds() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        var keys = new List<string>();
        BindLikeTheDesktop(keys, key => key);
        CoreFlash.Start();
        MainShellWindow.StartEffect(() => { });
        Dispatcher.UIThread.RunJobs();

        s.PanicKey = "NumLock";   // the 2 s watch has not run yet: the Escape session is still open
        setClock(5000);
        MainShellWindow.PortalActivated(keys[0], shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, shell.PanicPressCountForTest);
        Assert.True(CoreFlash.IsRunning);

        shell.OnX11PanicPress();   // NumLock through XInput2 works at once
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
        Assert.False(CoreFlash.IsRunning);
    });

    [Fact]
    public void DesktopKeepsTheOldKey_TheGrabIsReleased_NotAskedAgain_AndTheChosenKeyStillPanics() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "NumLock";   // rebound while nothing ran; the desktop still holds Escape for CCP
        var keys = new List<string>();
        BindLikeTheDesktop(keys, _ => "Esc");
        CoreFlash.Start();
        MainShellWindow.StartEffect(() => { });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "NumLock" }, keys);
        Assert.False(PortalPanicShortcut.IsBound);   // Escape is let go, not left consumed desktop-wide

        setClock(5000);
        MainShellWindow.PortalActivated(keys[0], shell);   // Escape from that session before the close lands
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, shell.PanicPressCountForTest);
        Assert.True(CoreFlash.IsRunning);

        MainShellWindow.ReleasePortalWhenUnused(shell);
        bool started = false;
        MainShellWindow.StartEffect(() => started = true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(started);              // effects start at once
        Assert.Single(keys);               // and the desktop is not asked again this run

        shell.OnX11PanicPress();           // the chosen key still panics in CCP windows
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
        Assert.False(CoreFlash.IsRunning);
    });

    [Fact]
    public void DisableThenEnable_BindsTheKeyChosenMeanwhile_AndOnlyThatKey() => Run((shell, setClock) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        var keys = new List<string>();
        BindLikeTheDesktop(keys, key => key);
        CoreFlash.Start();
        MainShellWindow.StartEffect(() => { });
        Dispatcher.UIThread.RunJobs();
        Assert.True(PortalPanicShortcut.IsBound);

        s.PanicKeyEnabled = false;
        Pump(() => !PortalPanicShortcut.IsBound);
        Assert.False(PortalPanicShortcut.IsBound);

        s.PanicKey = "F8";
        s.PanicKeyEnabled = true;
        Pump(() => keys.Count == 2 && PortalPanicShortcut.IsBound);
        Assert.Equal(new[] { "Escape", "F8" }, keys);
        Assert.Equal(PortalPanicShortcut.IdFor("F8"), PortalPanicShortcut.BoundId);

        setClock(5000);
        MainShellWindow.PortalActivated(keys[0], shell);   // the Escape session's late report
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, shell.PanicPressCountForTest);
        MainShellWindow.PortalActivated(keys[1], shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, shell.PanicPressCountForTest);
        Assert.False(CoreFlash.IsRunning);
    });

    // A portal stand-in: each bind opens a session that listens for that key's own id, and answers with the
    // trigger <paramref name="trigger"/> gives for the asked key (the desktop's trigger_description).
    private static void BindLikeTheDesktop(List<string> keys, Func<string, string> trigger)
    {
        PortalPanicShortcut.BindForTests = (_, up) =>
        {
            var key = MainShellWindow.PortalKeyForTest ?? "";
            keys.Add(key);
            PortalPanicShortcut.SetSessionForTests("/test/id-" + keys.Count, up, PortalPanicShortcut.IdFor(key));
            return Task.FromResult<string?>(trigger(key));
        };
    }

    private static void Pump(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(10); }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(Action<MainShellWindow, Action<long>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.PanicKeyEnabled = true;
        CoreSettings.Current.MasterVolume = 0;
        var oldTeardown = PanicWatchdog.Teardown;
        PanicWatchdog.Teardown = () => { };
        var oldCapturing = MainShellWindow.CapturingPanicKey;
        ResetPortal();
        MainShellWindow? shell = null;
        try
        {
            long clock = 1000;
            shell = new MainShellWindow { PanicRouteClock = () => clock };
            Dispatcher.UIThread.RunJobs();
            body(shell, t => clock = t);
        }
        finally
        {
            MainShellWindow.CapturingPanicKey = oldCapturing;
            CoreFlash.Stop();
            shell?.LeashHoldReleased();
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            PortalPanicShortcut.BindForTests = null;
            PortalPanicShortcut.UnbindAsync().GetAwaiter().GetResult();
            ResetPortal();
            PanicWatchdog.Teardown = oldTeardown;
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    // The portal bookkeeping is static (one session per process): start and end every test from a clean slate.
    private static void ResetPortal()
    {
        const BindingFlags F = BindingFlags.Static | BindingFlags.NonPublic;
        var t = typeof(MainShellWindow);
        (t.GetField("_portalWatch", F)!.GetValue(null) as DispatcherTimer)?.Stop();
        t.GetField("_portalWatch", F)!.SetValue(null, null);
        t.GetField("_portalBind", F)!.SetValue(null, null);
        t.GetField("_portalFailed", F)!.SetValue(null, false);
        t.GetField("_portalKey", F)!.SetValue(null, null);
        t.GetField("_portalRebinding", F)!.SetValue(null, false);
        t.GetField("_portalWrongKey", F)!.SetValue(null, false);
        PortalPanicShortcut.SetSessionForTests(null, null);
    }
}
