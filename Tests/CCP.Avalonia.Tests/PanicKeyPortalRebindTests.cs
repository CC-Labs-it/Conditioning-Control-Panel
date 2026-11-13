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
    }
}
