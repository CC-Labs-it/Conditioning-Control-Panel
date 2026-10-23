using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>feat-leash-receipts-hold (WPF MainWindow.Leash.cs:308-358, LeashHoldRing.cs, LeashCutConfirmWindow.cs,
/// LeashHolderCard.cs SentSection): holding the panic key on a leash shows 5..1, a release before five seconds
/// never asks or cuts, five seconds asks, only Yes cuts; the panic itself still fires on the first press.</summary>
public sealed class LeashHoldTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Targets : LeashCutSafety.ITargets
    {
        public bool LockdownActive => false;
        public void EndLockdown() { }
        public void DiscardLockdownRecovery() { }
        public bool RemoteActive => false;
        public void EndRemote() { }
        public bool StrictVideoRunning => false;
        public void StopVideo() { }
        public void ReleaseSafetySettings() { }
        public bool DropUnpushedLeashBookings() => false;
    }

    /// <summary>Holds for <paramref name="ms"/> on the shell's 250 ms clock (the X11 path: one press, then ticks).</summary>
    private static void Hold(MainShellWindow shell, Clock clock, int ms)
    {
        shell.OnX11PanicPress();                        // the X11 listener's press: panic (off here), then the hold
        Dispatcher.UIThread.RunJobs();
        for (int t = 250; t <= ms; t += 250)
        {
            clock.Now = clock.Now.AddMilliseconds(250);
            shell.LeashHoldClockTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Release(Clock clock)
    {
        X11PanicKey.PanicReleased!();                   // what the X11 listener raises on the raw key-up
        Dispatcher.UIThread.RunJobs();
        clock.Now = clock.Now.AddSeconds(5);
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void HeldPanicKey_ReleaseEarlyNeverAsks_FiveSecondsAsks_OnlyYesCuts() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        (CoreSettings.Current.Welcomed, CoreSettings.Current.HasAcceptedAgeVerification, CoreSettings.Current.PanicKeyEnabled) = (true, true, false);
        var (oldSvc, oldTime, oldReleased) = (LeashHead.Service, MainShellWindow.LeashHoldTime, X11PanicKey.PanicReleased);
        var oldKeyDown = MainShellWindow.LeashHoldKeyDown;
        bool keyDown = true;
        MainShellWindow.LeashHoldKeyDown = () => keyDown;
        var (oldTargets, oldUi) = (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi);
        var clock = new Clock();
        var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        (LeashHead.Service, MainShellWindow.LeashHoldTime) = (svc, clock);
        (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi) = (new Targets(), a => a());
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();              // the ctor wires the hold (InitializeLeashHold)
            shell.Show();
            Dispatcher.UIThread.RunJobs();

            // 4.75 s: the ring counts down to 1; the key comes up; nothing is asked, nothing is cut.
            Hold(shell, clock, 2000);
            Assert.True(LeashHoldRing.IsShown);
            Assert.Equal(3, LeashHoldRing.Number);
            Hold2(shell, clock);
            Assert.Equal(1, LeashHoldRing.Number);
            Release(clock);
            Assert.False(LeashHoldRing.IsShown);
            Assert.Null(LeashCutConfirmWindow.Current);
            Assert.NotNull(svc.Snapshot.Me);

            // A lost key-up (focus left for a native Wayland window): the next tick reads the key as up,
            // the hold ends, and six seconds later nothing has asked.
            Hold(shell, clock, 2000);
            Assert.True(LeashHoldRing.IsShown);
            keyDown = false;
            Hold2(shell, clock);
            Assert.False(LeashHoldRing.IsShown);
            Assert.Null(LeashCutConfirmWindow.Current);
            keyDown = true;
            clock.Now = clock.Now.AddSeconds(5);

            // Five seconds: the question, ring gone. No keeps the leash.
            var (oldAudio, oldArt, oldSuppressed) = (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider);
            var cue = System.IO.Path.GetTempFileName();
            var sounds = new System.Collections.Generic.List<string>();
            try
            {
                CoreSettings.Current.MasterVolume = 100;
                CoreAudio.IsOutputSuppressedProvider = () => false;
                CoreModArt.AudioOverridePathProvider = _ => cue;
                CoreAudio.PlayOneShotProvider = (_, volume, tag, _, _) => { Assert.Equal(0.10f, volume); sounds.Add(tag); };
                Hold(shell, clock, 5000);
                Assert.Equal(new[] { "leash-tick", "leash-tick", "leash-tick", "leash-tick" }, sounds);
            }
            finally
            {
                (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider) = (oldAudio, oldArt, oldSuppressed);
                System.IO.File.Delete(cue);
            }
            Assert.False(LeashHoldRing.IsShown);
            var ask = LeashCutConfirmWindow.Current!;
            Assert.NotNull(ask);
            Assert.Equal(Loc.GetF("leash_cut_confirm_body", "Vex"), ask.GetLogicalDescendants().OfType<TextBlock>().ElementAt(1).Text);
            Assert.Equal(Color.FromRgb(0x3A, 0x1F, 0x66), ((ISolidColorBrush)ask.NoButton.Resources["ButtonBackgroundPointerOver"]!).Color);
            Assert.Same(F.Pink, ask.YesButton.Resources["ButtonBackgroundPointerOver"]);
            Assert.Same(ask.YesButton.Foreground, ask.YesButton.Resources["ButtonForegroundPressed"]);
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            ask.RaiseEvent(key);
            Assert.True(key.Handled);
            Assert.Same(ask, LeashCutConfirmWindow.Current);
            Click(ask.NoButton);
            Release(clock);
            Assert.Null(LeashCutConfirmWindow.Current);
            Assert.NotNull(svc.Snapshot.Me);

            // Five seconds again: Yes cuts.
            Hold(shell, clock, 5000);
            // The existing cut path must close even a locked punishment before cutting.
            var oldWeb = LeashPunishWindow.CreateWeb;
            try
            {
                LeashPunishWindow.CreateWeb = false;
                Assert.True(LeashPunishWindow.OpenUrl("https://example.invalid", "Vex", true));
                Click(LeashCutConfirmWindow.Current!.YesButton);
                Assert.Null(LeashPunishWindow.Current);
            }
            finally { LeashPunishWindow.CloseNow(); LeashPunishWindow.CreateWeb = oldWeb; }
            Release(clock);
            Assert.Null(svc.Snapshot.Me);

            // Off the leash a hold shows nothing and asks nothing.
            Hold(shell, clock, 6000);
            Assert.False(LeashHoldRing.IsShown);
            Assert.Null(LeashCutConfirmWindow.Current);
        }
        finally
        {
            LeashCutConfirmWindow.Current?.Close();
            LeashHoldRing.Dismiss();
            shell?.Close();
            (LeashHead.Service, MainShellWindow.LeashHoldTime) = (oldSvc, oldTime);
            X11PanicKey.PanicReleased = oldReleased;
            MainShellWindow.LeashHoldKeyDown = oldKeyDown;
            (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi) = (oldTargets, oldUi);
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public void PortalHold_UsesReleaseRatherThanX11State_AndOnlyAsksAfterFiveSeconds() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.PanicKeyEnabled = false;
        CoreSettings.Current.MasterVolume = 0;
        var (oldSvc, oldClock, oldKey) = (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown);
        var clock = new Clock();
        var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (svc, clock, () => false);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            // A portal that has never sent Deactivated: the hold falls back to X11 key state (here: up),
            // so a press with no release can never ask.
            PortalPanicShortcut.SetSessionForTests("/test/session", null);
            shell.OnPortalPanicPressed();
            for (int t = 250; t <= 6000; t += 250) { clock.Now += TimeSpan.FromMilliseconds(250); shell.LeashHoldClockTick(); }
            Assert.Null(LeashCutConfirmWindow.Current);
            Assert.False(LeashHoldRing.IsShown);
            // The compositor proves it reports release: from now on only Deactivated ends a portal hold.
            PortalPanicShortcut.Route("/test/session", "panic", false, () => { }, () => { });
            clock.Now += TimeSpan.FromSeconds(2);
            shell.OnPortalPanicPressed();
            for (int t = 250; t <= 2000; t += 250) { clock.Now += TimeSpan.FromMilliseconds(250); shell.LeashHoldClockTick(); }
            X11PanicKey.PanicReleased?.Invoke(); // focus transfer synthesizes an XWayland release
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, LeashHoldRing.Number); // only portal Deactivated ends this hold.
            shell.OnPortalPanicReleased();
            clock.Now += TimeSpan.FromSeconds(6);
            shell.LeashHoldClockTick();
            Assert.False(LeashHoldRing.IsShown);
            Assert.Null(LeashCutConfirmWindow.Current);
            shell.OnPortalPanicPressed();
            for (int t = 250; t <= 5000; t += 250) { clock.Now += TimeSpan.FromMilliseconds(250); shell.LeashHoldClockTick(); }
            Assert.NotNull(LeashCutConfirmWindow.Current);
            Assert.NotNull(svc.Snapshot.Me);
            Click(LeashCutConfirmWindow.Current!.NoButton);
            shell.OnPortalPanicReleased();
            Assert.NotNull(svc.Snapshot.Me);
        }
        finally
        {
            LeashCutConfirmWindow.Current?.Close();
            LeashHoldRing.Dismiss();
            shell?.Close();
            PortalPanicShortcut.SetSessionForTests(null, null);
            (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (oldSvc, oldClock, oldKey);
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    /// <summary>A leashed shell with a bound test portal session (no D-Bus) and the X11 key state <paramref name="key"/>.</summary>
    private static void WithPortalShell(Func<bool?> key, Action<MainShellWindow, Clock> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (!OperatingSystem.IsLinux()) return;   // the GlobalShortcuts portal and the hold clock are Linux-only
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        (CoreSettings.Current.PanicKeyEnabled, CoreSettings.Current.MasterVolume) = (false, 0);
        var (oldSvc, oldClock, oldKey) = (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown);
        var clock = new Clock();
        var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (svc, clock, key);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            Dispatcher.UIThread.RunJobs();            // the ctor's queued work runs here, not inside a later test's hold
            PortalPanicShortcut.SetSessionForTests("/test/session", null);
            body(shell, clock);
        }
        finally
        {
            LeashCutConfirmWindow.Current?.Close();
            LeashHoldRing.Dismiss();
            shell?.LeashHoldReleased();               // a never-shown window raises no Closed, so stop its clock here
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            PortalPanicShortcut.SetSessionForTests(null, null);
            (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (oldSvc, oldClock, oldKey);
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public void UnprovenPortalHold_NoX11Listener_EndsOnFirstTick_NeverAsks() => WithPortalShell(() => null, (shell, clock) =>
    {
        // A portal that never sent Deactivated, and no X11 listener (key state unknown): treat as released.
        shell.OnPortalPanicPressed();
        Assert.True(MainShellWindow.KeepPortalBound);
        for (int t = 250; t <= 6000; t += 250) { clock.Now += TimeSpan.FromMilliseconds(250); shell.LeashHoldClockTick(); }
        Dispatcher.UIThread.RunJobs();
        Assert.Null(LeashCutConfirmWindow.Current);
        Assert.False(LeashHoldRing.IsShown);
        Assert.False(MainShellWindow.KeepPortalBound);
    });

    [Fact]
    public void LostDeactivated_HoldEnds_PortalNoLongerKeptBound_NextPressStillPanics() => WithPortalShell(() => false, (shell, clock) =>
    {
        var presses = 0;
        Action down = () => { presses++; shell.OnPortalPanicPressed(); };
        PortalPanicShortcut.Route("/test/session", "panic", true, down, () => { }, clock.Now.UtcDateTime);
        Assert.True(PortalPanicShortcut.IsHeld);
        Assert.True(MainShellWindow.KeepPortalBound);   // a live hold keeps the session until it ends
        clock.Now += TimeSpan.FromMilliseconds(250);
        shell.LeashHoldClockTick();                     // X11 says up; no Deactivated ever arrives
        Assert.True(PortalPanicShortcut.IsHeld);        // the portal still believes it is held ...
        Assert.False(MainShellWindow.KeepPortalBound);  // ... but no effect and no hold: the grab goes
        PortalPanicShortcut.Route("/test/session", "panic", true, down, () => { }, (clock.Now + LeashHoldToCut.RepeatGap * 2).UtcDateTime);
        Assert.Equal(2, presses);                       // the panic key never goes dead
    });

    /// <summary>Continues a running hold from 2 s to 4.75 s.</summary>
    private static void Hold2(MainShellWindow shell, Clock clock)
    {
        for (int t = 2250; t <= 4750; t += 250)
        {
            clock.Now = clock.Now.AddMilliseconds(250);
            shell.LeashHoldClockTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The X11 listener's routing: the bound key's press still runs the panic on the first press;
    /// its release only ends a hold; any other key does neither.</summary>
    [Fact]
    public void X11Route_PanicFiresOnTheFirstPress_ReleaseOnlyEndsTheHold()
    {
        var old = X11PanicKey.PanicReleased;
        var oldCode = X11PanicKey.BoundKeycode;
        int presses = 0, releases = 0;
        try
        {
            X11PanicKey.PanicReleased = () => releases++;
            X11PanicKey.BindKeycodeForTest(9);
            Assert.True(X11PanicKey.Route(true, 9, () => presses++));
            Assert.Equal((1, 0), (presses, releases));
            Assert.True(X11PanicKey.Route(false, 9, () => presses++));
            Assert.Equal((1, 1), (presses, releases));
            Assert.False(X11PanicKey.Route(true, 10, () => presses++));
            Assert.Equal((1, 1), (presses, releases));
        }
        finally { X11PanicKey.PanicReleased = old; X11PanicKey.BindKeycodeForTest(oldCode); }
    }

    /// <summary>WPF SentSection/SentRow: nothing until the server speaks receipts; then the newest four rows,
    /// each with its lit step dots, step word and kind tag.</summary>
    [Fact]
    public void SentList_DrawsNothingWithoutReceipts_ThenFourRowsWithSteps() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Null(LeashSentList.For(null, "u_vex"));
        Assert.Null(LeashSentList.For(new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me"), "u_vex"));
        var now = DateTimeOffset.UtcNow;
        var items = Enumerable.Range(0, 6).Select(i =>
            new LeashSentItem("l" + i, LeashItemKind.Punish, "u_vex", null, i == 0 ? LeashStep.Seen : LeashStep.Sent, now.AddMinutes(-i), now.AddMinutes(-i))
            { Punish = PunishKind.Lines, Size = 3, SeenAt = i == 0 ? now : null }).ToList();
        var list = new LeashSentList(items, now);
        var rows = list.Children.OfType<Grid>().ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal("leash-sent-row:punish:seen", rows[0].Tag);
        var dots = rows[0].GetLogicalDescendants().OfType<Ellipse>().Select(e => (string)e.Tag!).ToArray();
        Assert.Equal(new[] { "leash-step-dot:0:on", "leash-step-dot:1:on", "leash-step-dot:2:on", "leash-step-dot:3:off" }, dots);
        var word = rows[0].GetLogicalDescendants().OfType<TextBlock>().Single(t => (string?)t.Tag == "leash-step-word");
        Assert.Equal(Loc.Get("leash_step_seen"), word.Text);
    });
}
