using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>wire-win-emi-ring-c: the ambient nudge poll, the cue silence gate and the book dodge (WPF parity).</summary>
public sealed class EmiRingFollowUpTests
{
    private const string Empty = "{\"moments\":{},\"pools\":{}}";

    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>WPF StartNudges/OnNudgeTick: a summon starts the 5 s poll; the pet nudge is owed 25 s
    /// after the summon and spends one fire; dismissal stops the poll.</summary>
    [Fact]
    public Task SummonPollsThePetNudgeAt25SecondsAndDismissStopsThePoll() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var clock = new StepClock();
        var service = new EmiDeskService { LineEngine = EmiLineEngine.FromJson(Empty), Clock = clock };
        var st = EmiState.Current;
        var old = (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar, st.PetGistGot, st.PetsTotal);
        bool had = st.NudgeFires.TryGetValue(EmiNudgeMachine.PetTrack, out var previousFires);
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            st.PetGistGot = false; st.PetsTotal = 0;
            st.NudgeFires.Remove(EmiNudgeMachine.PetTrack);
            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            clock.Advance(400); clock.Advance(260);       // smoke lead + CRT power-on
            Assert.False(desk.Transiting);
            for (int i = 0; i < 4; i++) clock.Advance(EmiDeskService.NudgePollMs);   // t = 20.66 s
            Assert.False(st.NudgeFires.ContainsKey(EmiNudgeMachine.PetTrack));
            Assert.False(desk.ChainLive);
            clock.Advance(EmiDeskService.NudgePollMs);   // the 25 s poll
            Assert.Equal(1, st.NudgeFires[EmiNudgeMachine.PetTrack]);
            Assert.True(desk.ChainLive);
            service.Dismiss();
            for (int i = 0; i < 100; i++) clock.Advance(EmiDeskService.NudgePollMs);  // past the 240 s repeat
            Assert.Equal(1, st.NudgeFires[EmiNudgeMachine.PetTrack]);
        }
        finally
        {
            service.Window?.ShutDown();
            (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar, st.PetGistGot, st.PetsTotal) = old;
            if (had) st.NudgeFires[EmiNudgeMachine.PetTrack] = previousFires;
            else st.NudgeFires.Remove(EmiNudgeMachine.PetTrack);
        }
    });

    /// <summary>WPF EmiSfx.Audible: a suppressed endpoint and an active hold each silence the ring cue.</summary>
    [Fact]
    public Task RingCueIsSilentWhileOutputIsSuppressedOrAHoldIsActive() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var played = new List<string>();
        var old = (CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider, CoreSettings.Current.MasterVolume, service.LineEngine);
        var clock = new StepClock();
        var engine = EmiLineEngine.FromJson("{\"moments\":{\"h\":{\"hold\":true,\"holdUntilReleased\":true}},\"pools\":{}}");
        try
        {
            CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) => played.Add(tag);
            CoreSettings.Current.MasterVolume = 50;
            EmiSfx.Clock = clock;
            service.LineEngine = engine;

            CoreAudio.IsOutputSuppressedProvider = () => true;
            EmiSfx.RingOpen();
            Assert.Empty(played);

            CoreAudio.IsOutputSuppressedProvider = null;
            engine.Draw("h");
            Assert.True(engine.HoldActive);
            clock.Advance(200);
            EmiSfx.RingOpen();
            Assert.Empty(played);

            engine.ReleaseHold("h");
            clock.Advance(200);
            EmiSfx.RingOpen();
            Assert.Equal(new[] { "emi-sfx-ring" }, played);
        }
        finally
        {
            (CoreAudio.PlayOneShotProvider, CoreAudio.IsOutputSuppressedProvider, CoreSettings.Current.MasterVolume, service.LineEngine) = old;
            EmiSfx.Clock = TimeProvider.System;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF LayoutBubble book dodge: an open book on her right sends the line to her left; folding brings it back.</summary>
    [Fact]
    public Task OpenBookOnHerRightFlipsHerLineAndFoldingBringsItBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var st = EmiState.Current;
        var (bookmark, opens) = (st.BookCard, st.CodexOpens);
        var desk = new EmiDeskWindow();
        try
        {
            desk.Position = new PixelPoint(0, 0);     // room on her right, so the book takes that side
            desk.Show();
            desk.Say("which side");
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.SpeechFlipped);
            desk.OpenBook();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, desk.Book!.SideOfHer);
            Assert.True(desk.SpeechFlipped);
            desk.CloseBook();
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.SpeechFlipped);
        }
        finally
        {
            desk.CloseBook();
            desk.ShutDown();
            (st.BookCard, st.CodexOpens) = (bookmark, opens);
        }
        return Task.CompletedTask;
    });

    /// <summary>Manual clock: due timers fire only on <see cref="Advance"/>; wall time steps with it.</summary>
    private sealed class StepClock : TimeProvider
    {
        private long _now;
        private readonly List<(long Due, TimerCallback Cb, object? State, Stub Timer)> _timers = new();
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(1).AddMilliseconds(_now);
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
}
