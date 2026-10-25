using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Controls.Presenters;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Newtonsoft.Json.Linq;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>feat-leash-receipts-hold (WPF MainWindow.Leash.cs:308-358, LeashHoldRing.cs, LeashCutConfirmWindow.cs,
/// LeashHolderCard.cs SentSection): holding the panic key on a leash shows 5..1, a release before five seconds
/// never asks or cuts, five seconds asks, only Yes cuts; the panic itself still fires on the first press.</summary>
public sealed class LeashHoldTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

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
        var shell = new MainShellWindow();              // the ctor wires the hold (InitializeLeashHold)
        try
        {
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
            Hold(shell, clock, 5000);
            Assert.False(LeashHoldRing.IsShown);
            var ask = LeashCutConfirmWindow.Current!;
            Assert.NotNull(ask);
            Assert.Equal(Loc.GetF("leash_cut_confirm_body", "Vex"), ask.GetLogicalDescendants().OfType<TextBlock>().ElementAt(1).Text);
            Click(ask.NoButton);
            Release(clock);
            Assert.Null(LeashCutConfirmWindow.Current);
            Assert.NotNull(svc.Snapshot.Me);

            // Five seconds again: Yes cuts and closes the punishment video through the existing host.
            var oldWeb = LeashPunishWindow.CreateWeb;
            LeashPunishWindow.CreateWeb = false;
            try { Assert.True(LeashPunishWindow.OpenUrl("https://example.invalid/video", "Vex", true)); }
            finally { LeashPunishWindow.CreateWeb = oldWeb; }
            Hold(shell, clock, 5000);
            Click(LeashCutConfirmWindow.Current!.YesButton);
            Release(clock);
            Assert.Null(svc.Snapshot.Me);
            Assert.Null(LeashPunishWindow.Current);

            // Off the leash a hold shows nothing and asks nothing.
            Hold(shell, clock, 6000);
            Assert.False(LeashHoldRing.IsShown);
            Assert.Null(LeashCutConfirmWindow.Current);
        }
        finally
        {
            LeashCutConfirmWindow.Current?.Close();
            LeashHoldRing.Dismiss();
            LeashPunishWindow.CloseNow();
            shell.Close();
            (LeashHead.Service, MainShellWindow.LeashHoldTime) = (oldSvc, oldTime);
            X11PanicKey.PanicReleased = oldReleased;
            MainShellWindow.LeashHoldKeyDown = oldKeyDown;
            (LeashCutSafety.LiveTargets, LeashCutSafety.RunOnUi) = (oldTargets, oldUi);
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public void PortalHold_UsesReleaseInsteadOfX11Keymap_AndPanicDismissesConfirmation() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var oldSvc = LeashHead.Service;
        var oldTime = MainShellWindow.LeashHoldTime;
        var oldKey = MainShellWindow.LeashHoldKeyDown;
        var svc = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        svc.ApplyBlock(LeashHeadTests.BlockWithPunishment());
        var clock = new Clock();
        (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (svc, clock, () => false);
        var shell = new MainShellWindow();
        try
        {
            shell.LeashPortalPressed();
            for (int i = 0; i < 8; i++) { clock.Now = clock.Now.AddMilliseconds(250); shell.LeashHoldClockTick(); }
            Assert.True(LeashHoldRing.IsShown); // X11 cannot see a native Wayland key.
            Assert.False(MainShellWindow.PortalMayUnbind(shell)); // First panic stopped effects; hold must survive the two-second watcher.
            shell.LeashPortalReleased();
            Assert.True(MainShellWindow.PortalMayUnbind(shell));
            Assert.False(LeashHoldRing.IsShown);
            clock.Now = clock.Now.AddSeconds(5);
            shell.LeashPortalPressed();
            for (int i = 0; i < 20; i++) { clock.Now = clock.Now.AddMilliseconds(250); shell.LeashHoldClockTick(); }
            var ask = LeashCutConfirmWindow.Current;
            Assert.NotNull(ask);
            // The shared surface is registered, and a panic never accepts its Yes.
            PanicSurfaces.All.Single(x => x.Id == "leash-confirm").Stop(shell);
            Assert.Null(LeashCutConfirmWindow.Current);
            Assert.NotNull(svc.Snapshot.Me);
        }
        finally
        {
            shell.LeashPortalReleased();
            LeashCutConfirmWindow.Current?.Close();
            shell.Close();
            (LeashHead.Service, MainShellWindow.LeashHoldTime, MainShellWindow.LeashHoldKeyDown) = (oldSvc, oldTime, oldKey);
        }
    });

    private sealed class HolderWire : ILeashApi
    {
        internal int Releases;
        public System.Threading.Tasks.Task<JObject?> CallAsync(string op, JObject body, System.Threading.CancellationToken ct = default)
        {
            if (op == "release") Releases++;
            return System.Threading.Tasks.Task.FromResult<JObject?>(new JObject { ["ok"] = true });
        }
    }

    [Fact]
    public void HolderPage_RefreshesReceipts_ReleaseRequiresYes_AndUsesWpfHoverColors() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var wire = new HolderWire();
        var svc = new LeashService(wire, () => "u_me");
        var block = LeashHeadTests.BlockWithPunishment();
        block["me"] = null;
        block["holding"] = JArray.Parse("""[{"who":{"id":"u_pet","name":"Pet"},"online":true,"intensity":"soft","since":"2026-09-26T00:00:00Z","day":1,"week":[],"pending":[],"punished_today":0}]""");
        svc.ApplyBlock(block);
        var page = new LeashTabView();
        page.BindReceipts(svc);
        try
        {
            Assert.False(page.ShowingEmpty);
            var card = page.GetLogicalDescendants().OfType<LeashReceiptCard>().Single();
            Click(card.ReleaseButton);
            var ask = LeashCutConfirmWindow.Current!;
            Assert.Equal(Loc.GetF("leash_release_confirm_title", "Pet"), ask.Title);
            Color Hover(Button button)
            {
                ask.UpdateLayout();
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), ask);
                Assert.NotNull(point);
                ask.MouseMove(point.Value);
                Dispatcher.UIThread.RunJobs();
                return ((ISolidColorBrush)button.GetVisualDescendants().OfType<ContentPresenter>()
                    .First(x => x.Name == "PART_ContentPresenter").Background!).Color;
            }
            Assert.Equal(Color.Parse("#3A1F66"), Hover(ask.NoButton));
            Assert.Equal(Color.Parse("#FF5FB4"), Hover(ask.YesButton));
            Click(ask.NoButton);
            Assert.Single(svc.Snapshot.Holding);
            Assert.Equal(0, wire.Releases);
            // A send raises ReceiptsChanged and rebuilds the hosted receipt list.
            svc.ApplyReceipts(new[] { new LeashReceipt("0123456789abcdef", LeashItemKind.Tug, "u_pet", "Pet", LeashStep.Seen, DateTimeOffset.UtcNow) });
            card = page.GetLogicalDescendants().OfType<LeashReceiptCard>().Single();
            Assert.Single(card.GetLogicalDescendants().OfType<LeashSentList>());
            Click(card.ReleaseButton);
            Click(LeashCutConfirmWindow.Current!.YesButton);
            Assert.Equal(1, wire.Releases);
            block["holding"] = new JArray(); // WPF keeps the card until the confirming poll.
            svc.ApplyBlock(block);
            Assert.Empty(svc.Snapshot.Holding);
            Assert.True(page.ShowingEmpty);
        }
        finally { page.BindReceipts(null); LeashCutConfirmWindow.Current?.Close(); }
    });

    [Fact]
    public void Tick_UsesMasterVolumeAndModCue_AndRespectsSuppressedOutput() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var path = System.IO.Path.GetTempFileName();
        var oldPath = CoreModArt.AudioOverridePathProvider;
        var oldPlay = CoreAudio.PlayOneShotProvider;
        var oldSuppressed = CoreAudio.IsOutputSuppressedProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var oldClock = LeashSound.Clock;
        LeashSound.Clock = new Clock();
        int played = 0;
        try
        {
            CoreSettings.Current.MasterVolume = 50;
            CoreModArt.AudioOverridePathProvider = rel => { Assert.Equal("chaos/countdown_tick.mp3", rel); return path; };
            CoreAudio.PlayOneShotProvider = (file, volume, tag, _, _) =>
            {
                Assert.Equal(path, file);
                Assert.Equal(0.05f, volume);
                Assert.Equal("leash-tick", tag);
                played++;
            };
            CoreAudio.IsOutputSuppressedProvider = () => true;
            LeashSound.Tick();
            Assert.Equal(0, played);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            LeashSound.Tick();
            Assert.Equal(1, played);
            LeashSound.Tick(); // WPF global minimum gap.
            Assert.Equal(1, played);
        }
        finally
        {
            LeashSound.Clock = oldClock;
            CoreModArt.AudioOverridePathProvider = oldPath;
            CoreAudio.PlayOneShotProvider = oldPlay;
            CoreAudio.IsOutputSuppressedProvider = oldSuppressed;
            CoreSettings.Current.MasterVolume = oldVolume;
            System.IO.File.Delete(path);
        }
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
