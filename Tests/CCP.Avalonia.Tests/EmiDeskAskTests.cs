using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDeskWindow.Bubble.cs ShowAsk/BuildChips/AnswerAsk and EmiCodex.MaybeOfferSoon, from the summon.</summary>
public sealed class EmiDeskAskTests
{
    // bookOffer as shipped, plus `scripted` so an in-memory engine (summon count 0) passes the cadence floor.
    private const string Lines = """
    {"moments":{"bookOffer":{"pools":["bookOffer"],"odds":1.0,"priority":3,"askOdds":1.0,"scripted":true,
        "spiceCeiling":2,"limit":{"per":"ever","max":1}}},
     "pools":{"bookOffer":[{"id":"bookOffer.001","t":"there's a manual now.","face":"^_^","spice":0}]},
     "asks":[{"id":"ask.bookOffer.001","moment":"bookOffer","q":"want the manual?","face":"o_o",
        "chips":["show me","not now"],"yes":{"t":"good. start anywhere.","face":"^_~"},
        "no":{"t":"not now works.","face":"._."},"spice":0,"effect":"book:open"}]}
    """;

    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static IEnumerable<string> Bubble(EmiDeskWindow desk) =>
        desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    [Fact]
    public Task SecondSummonOffersTheBookAfterTwentySecondsAndYesOpensIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var (oldEngine, oldClock) = (service.LineEngine, service.Clock);
        var (oldSituation, oldFeasible) = (EmiLineEngine.AskSituationProbe, EmiLineEngine.EffectFeasibleProbe);
        var st = EmiState.Current;
        var s = CoreSettings.Current;
        var old = (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskOffers, st.SummonCount, st.CodexOpens);
        var clock = new StepClock();
        var moments = new List<string>();
        EventHandler<EmiMoment> observer = (_, m) => moments.Add(m.Id);
        try
        {
            service.Window?.ShutDown();
            (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskOffers) = (true, false, true);
            st.SummonCount = 1; st.CodexOpens = 0;   // this summon is the second; the book is unread
            service.LineEngine = EmiLineEngine.FromJson(Lines);
            service.Clock = clock;
            EmiDeskService.SeedEngineProbes();   // what App startup does
            Assert.True(EmiDeskService.CodexHasContent);
            service.MomentFired += observer;

            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            clock.Advance(400); clock.Advance(260);   // smoke lead + CRT power-on
            Assert.False(desk.Transiting);
            clock.Advance(EmiDeskService.BookOfferDelayMs - 661);
            Assert.DoesNotContain("bookOffer", moments);   // not before the 20 s beat
            clock.Advance(1);
            Assert.Contains("bookOffer", moments);
            Assert.True(desk.AskLive);
            Assert.Contains(".", Bubble(desk));
            desk.StepAsk(); desk.StepAsk(); desk.StepAsk();   // . / .. / ... then the question
            Assert.Contains("want the manual?", Bubble(desk));
            Assert.Equal("o_o", desk.Face);
            var chips = desk.AskChips;
            Assert.Equal(2, chips.Count);
            Assert.True(desk.BubbleHost.IsHitTestVisible);

            desk.SpeakRingLine(new LineDraw("late", "late", "parked line", "^_^", null, 2, false, 0), service.LineEngine);
            Assert.Contains("want the manual?", Bubble(desk));   // the offer owns the bubble

            chips[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(desk.AskLive);
            Assert.NotNull(desk.Book);
            Assert.Equal(1, st.CodexOpens);
            Assert.Contains("askAnswered", moments);
            Assert.Contains("effectFired", moments);
            Assert.Empty(desk.AskChips);
            Assert.False(desk.BubbleHost.IsHitTestVisible);
            Assert.False(service.EffectFeasible("book:open"));   // open book: no second offer chip
        }
        finally
        {
            service.MomentFired -= observer;
            service.Window?.CloseBook();
            service.Window?.ShutDown();
            (service.LineEngine, service.Clock) = (oldEngine, oldClock);
            (EmiLineEngine.AskSituationProbe, EmiLineEngine.EffectFeasibleProbe) = (oldSituation, oldFeasible);
            (s.EmiDeskEnabled, s.EmiDeskMuteAvatar, s.EmiDeskOffers, st.SummonCount, st.CodexOpens) = old;
        }
    });

    [Fact]
    public Task EscapeDropsTheQuestionWithAThinkingBeatThenReleasesTheParkedLine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var oldEngine = service.LineEngine;
        var desk = new EmiDeskWindow();
        try
        {
            service.LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
            desk.Show();
            var no = new LineDraw("n", "n", "fine.", "._.", null, 2, false, 0);
            desk.ShowAsk(new AskDraw("ask.t", "t", "story time?", "o_o", new[] { "go on", "nah" }, no, no, "none", null));
            desk.StepAsk(); desk.StepAsk(); desk.StepAsk();
            Assert.Equal(2, desk.AskChips.Count);
            Assert.False(desk.SpeakRingLine(new LineDraw("late", "late", "parked line", "^_^", null, 2, false, 0), service.LineEngine));
            desk.CancelChain();   // a chain cut mid-question must not take the question down
            Assert.Contains("story time?", Bubble(desk));

            desk.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(desk.AskLive);
            Assert.Empty(desk.AskChips);
            Assert.Equal("-_-", desk.Face);
            desk.StepAsk();
            Assert.Contains("...", Bubble(desk));
            desk.StepAsk();
            Assert.Equal("0_0", desk.Face);
            desk.StepAsk();   // the parked line's 900 ms beat
            Assert.True(desk.ChainLive || Bubble(desk).Any(t => t == "."));
        }
        finally
        {
            desk.ShutDown();
            service.LineEngine = oldEngine;
        }
        return Task.CompletedTask;
    });

    /// <summary>Manual clock: due timers fire only on <see cref="Advance"/>.</summary>
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
}
