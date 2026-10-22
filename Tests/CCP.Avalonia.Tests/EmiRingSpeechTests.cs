using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRingSpeechTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task SayUsesTheLockedDotCadenceAndCloseCutsTheBubble() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            desk.Say("a card stays when you pin it.");
            Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == ".");
            for (int i = 0; i < 16 && !desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>()
                     .Any(t => t.Text == "a card stays when you pin it."); i++)
                desk.StepSpeech(); // the dot frames, stepped instead of slept (P08)
            Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Text == "a card stays when you pin it.");
            if (Environment.GetEnvironmentVariable("CCP_EMI_RING_RENDER_PATH") is { Length: > 0 } output)
                desk.CaptureRenderedFrame()!.Save(output);
            desk.CancelChain();
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
    /// <summary>Run the summon's smoke lead (380 ms) and CRT power-on (240 ms) on the stepped clock.</summary>
    private static void EndSummon(StepClock clock, EmiDeskWindow desk)
    {
        clock.Advance(400); clock.Advance(260);
        Assert.False(desk.Transiting);
    }

    [Fact]
    public Task HoldCutsAPlayingLineAndReturnsToRestWithoutSpendingSpeech() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        var engine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
        try
        {
            desk.Show(); desk.Say("this must stop");
            Assert.False(desk.SpeakRingLine(new LineDraw("hold", "hold", "", "-_-", null, 3, true, 100), engine));
            Assert.Equal("-_-", desk.Face);
            Assert.False(desk.ChainLive);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
            desk.EndHold();
            Assert.Equal("0_0", desk.Face);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RealRingEmitsOpenDismissAndThirdIgnoreAndFirstPinRetiresOnboarding() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var oldEngine = service.LineEngine;
        var (oldClock, clock) = (service.Clock, new StepClock());
        var st = EmiState.Current;
        var old = (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar,
            st.RingIgnoreStreak, st.RingOpens, st.PinGistGot);
        var moments = new List<string>();
        EventHandler<EmiMoment> observer = (_, m) => moments.Add(m.Id);
        try
        {
            service.Window?.ShutDown();
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            st.RingIgnoreStreak = 0; st.PinGistGot = false;
            service.LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
            service.Clock = clock;   // the window is created by Summon below and takes it as FxClock
            service.MomentFired += observer;
            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            EndSummon(clock, desk);
            for (int i = 0; i < 3; i++)
            {
                desk.ToggleRing(); Assert.True(desk.RingOpen);
                desk.CloseRing();
                Assert.False(desk.RingOpen); // the close is bookkept at once; the fold only animates
            }
            Assert.Equal(old.RingOpens + 3, st.RingOpens);
            Assert.Equal(3, moments.Count(m => m == "ringOpen"));
            Assert.Equal(3, moments.Count(m => m == "ringDismissed"));
            Assert.Single(moments.Where(m => m == "suggestionIgnored3x"));
            Assert.Equal(0, st.RingIgnoreStreak);
            desk.NotePinMadeElsewhere("vault", false);
            Assert.False(st.PinGistGot);
            desk.NotePinMadeElsewhere("vault", true);
            Assert.True(st.PinGistGot);
            Assert.Single(moments.Where(m => m == "pinAdded"));
            desk.Say("panic cuts this");
            service.StopRingSpeechForPanic();
            Assert.False(desk.ChainLive);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
            Assert.Contains("panicPressed", moments);
            int uses = st.Usage.TryGetValue("settings", out var count) ? count : 0;
            EmiTargets.Find("settings")!.Open();
            Assert.Equal(uses + 1, st.Usage["settings"]);
            Assert.Contains("ringPick", moments);
        }
        finally
        {
            service.MomentFired -= observer;
            service.Window?.ShutDown();
            service.LineEngine = oldEngine;
            service.Clock = oldClock;
            (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar,
                st.RingIgnoreStreak, st.RingOpens, st.PinGistGot) = old;
        }
    });
    [Fact]
    public void TheShippedLinesFileSitsWhereTheEngineLooksAndSpeaksTheRingMoments()
    {
        // EmiLineEngine.FindLinesFile reads BaseDirectory/Resources/emi first; a shipped build has no source tree.
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json");
        Assert.True(File.Exists(path), path);
        var engine = EmiLineEngine.FromJson(File.ReadAllText(path));
        Assert.Contains("lockedCardTapped", engine.MomentIds);
        Assert.Contains("ringOpen", engine.MomentIds);
    }

    [Fact]
    public Task LockedCardAsksForTheTierAndFiresLockedCardTappedWithoutScoring() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var st = EmiState.Current;
        var (oldPremium, oldDenied) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler);
        var moments = new List<EmiMoment>();
        var denied = new List<TierVerdict>();
        EventHandler<EmiMoment> observer = (_, m) => moments.Add(m);
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = denied.Add;   // no toast reaches a desktop
            service.MomentFired += observer;
            int uses = st.Usage.TryGetValue("lockdown", out var n) ? n : 0;
            EmiTargets.Find("lockdown")!.Open();
            Assert.Single(denied);
            var tapped = Assert.Single(moments, m => m.Id == "lockedCardTapped");
            Assert.Equal("lockdown", EmiLineEngine.ToCtx(tapped.Context)["target"]?.ToString());
            Assert.DoesNotContain(moments, m => m.Id == "ringPick");   // a refused door is not a pick
            Assert.Equal(uses, st.Usage.TryGetValue("lockdown", out var after) ? after : 0);
        }
        finally
        {
            service.MomentFired -= observer;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldPremium, oldDenied);
        }
        return Task.CompletedTask;
    });

    /// <summary>Manual clock for the 900 ms nudge delay: due timers fire only on <see cref="Advance"/>.</summary>
    private sealed class StepClock : TimeProvider
    {
        private long _now;
        private readonly List<(long Due, TimerCallback Cb, object? State, Stub Timer)> _timers = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new Stub();
            _timers.Add((_now + (long)due.TotalMilliseconds, cb, state, t));
            return t;
        }
        public void Advance(int ms)
        {
            _now += ms;
            foreach (var t in _timers.Where(t => t.Due <= _now && !t.Timer.Disposed).ToList())
            { _timers.Remove(t); t.Cb(t.State); }
            Dispatcher.UIThread.RunJobs();
        }
        public sealed class Stub : ITimer
        {
            public bool Disposed;
            public bool Change(TimeSpan due, TimeSpan period) => false;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public Task DelayedPinNudgeSpendsOneFireAndDismissalCancelsThePendingAttempt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var clock = new StepClock();
        var service = new EmiDeskService { LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}"), Clock = clock };
        var st = EmiState.Current;
        var old = (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar, st.PinGistGot);
        bool had = st.NudgeFires.TryGetValue(EmiNudgeMachine.PinTrack, out var previousFires);
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            st.PinGistGot = false; st.NudgeFires.Remove(EmiNudgeMachine.PinTrack);
            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            EndSummon(clock, desk);
            service.NoteRingOpened();
            Assert.False(desk.ChainLive); // the 900 ms delay leaves the new cards unobscured
            clock.Advance(899);
            Assert.False(st.NudgeFires.ContainsKey(EmiNudgeMachine.PinTrack)); // not before 900 ms
            clock.Advance(1);
            Assert.Equal(1, st.NudgeFires[EmiNudgeMachine.PinTrack]);
            Assert.True(desk.ChainLive);
            desk.CancelChain();
            service.NoteRingOpened();
            clock.Advance(1100);
            Assert.Equal(1, st.NudgeFires[EmiNudgeMachine.PinTrack]);
            Assert.False(desk.ChainLive); // once per summon, no retry queue
            desk.Say("dismiss cuts this");
            service.Dismiss();
            Assert.False(desk.ChainLive);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
        }
        finally
        {
            service.Window?.ShutDown();
            (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar, st.PinGistGot) = old;
            if (had) st.NudgeFires[EmiNudgeMachine.PinTrack] = previousFires;
            else st.NudgeFires.Remove(EmiNudgeMachine.PinTrack);
        }

        // A fresh summon owns a fresh machine: dismiss before 900 ms, and no line or fire lands.
        var pendingClock = new StepClock();
        var pending = new EmiDeskService { LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}"), Clock = pendingClock };
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true; CoreSettings.Current.EmiDeskMuteAvatar = false;
            st.PinGistGot = false; st.NudgeFires.Remove(EmiNudgeMachine.PinTrack);
            await pending.Summon("pending-test");
            var desk = Assert.IsType<EmiDeskWindow>(pending.Window);
            EndSummon(pendingClock, desk);
            pending.NoteRingOpened(); pending.Dismiss();
            pendingClock.Advance(1100);
            Assert.False(desk.ChainLive);
            Assert.False(st.NudgeFires.ContainsKey(EmiNudgeMachine.PinTrack));
        }
        finally
        {
            pending.Window?.ShutDown();
            (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar, st.PinGistGot) = old;
            if (had) st.NudgeFires[EmiNudgeMachine.PinTrack] = previousFires;
            else st.NudgeFires.Remove(EmiNudgeMachine.PinTrack);
        }
    });

}
