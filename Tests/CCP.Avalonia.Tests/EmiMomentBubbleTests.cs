using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class EmiMomentBubbleTests
{
    private static LineDraw Line(string id) => new(id, "test", id, "^_^", null, 2, false, 0);
    private static AskDraw Ask() => new("test.ask", "test", "a question?", "^_^",
        new[] { "yes", "no" }, Line("reply.yes"), Line("reply.no"), "book:open", null);
    private static Task Run(Func<EmiDeskWindow, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        _ = EmiDeskService.Instance;
        var win = new EmiDeskWindow();
        try { win.Show(); await test(win); }
        finally { win.ShutDown(); Dispatcher.UIThread.RunJobs(); }
    });
    private sealed class Steps : TimeProvider
    {
        private sealed class Entry { public long Due; public Action Tick = () => { }; public bool Stopped; }
        private readonly List<Entry> _pending = new();
        private long _now;
        public Steps(EmiDeskWindow win) { win.StartTimer = Start; win.PatClock = this; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_now);
        public Action Start(int ms, Action tick)
        {
            var entry = new Entry { Due = _now + ms, Tick = tick };
            _pending.Add(entry);
            return () => entry.Stopped = true;
        }
        public void Advance(int ms)
        {
            long end = _now + ms;
            while (_pending.Where(e => !e.Stopped && e.Due <= end).OrderBy(e => e.Due).FirstOrDefault() is { } next)
            {
                _pending.Remove(next); _now = next.Due; next.Tick();
            }
            _now = end;
        }
    }
    [Fact]
    public void DeskLinesShipBesideTheExe() =>
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json")));
    [Fact]
    public Task AskTypesThenWaitsAndParksSpeechUntilAChip() => Run(win =>
    {
        var clock = new Steps(win); string? effect = null;
        win.RunAskEffect = value => { Assert.False(win.AskLive); effect = value; };
        win.ShowAsk(Ask()); Assert.Equal(".", win.MomentBubbleText);
        win.SpeakLine(Line("older"));
        win.SpeakLine(Line("newest")); Assert.Equal(".", win.MomentBubbleText);
        clock.Advance(1360); Assert.Equal("a question?", win.MomentBubbleText);
        win.SpeakLine(new LineDraw("hold", "test", "", "-_-", null, 2, true, 500));
        clock.Advance(1600); Assert.True(win.AskLive);
        Assert.Equal("a question?", win.MomentBubbleText);
        win.AnswerAsk(0); Assert.False(win.AskLive);
        Assert.Equal("book:open", effect); Assert.Equal(".", win.MomentBubbleText);
        clock.Advance(420); Assert.Equal("..", win.MomentBubbleText);
        clock.Advance(480 + 1360); Assert.Equal("newest", win.MomentBubbleText);
        return Task.CompletedTask;
    });
    [Fact]
    public Task BubbleWearsThePixelFontAndATail() => Run(win =>
    {
        new Steps(win);
        win.ShowAsk(Ask()); Assert.Contains("Press Start 2P", win.MomentBubbleTextBlock!.FontFamily.ToString());
        Assert.True(win.MomentBubbleTail is { IsVisible: true, Points.Count: 3 }); Assert.Same(win, TopLevel.GetTopLevel(win.MomentBubbleTail));
        Assert.True(win.MomentBubbleTail.IsEffectivelyVisible);
        return Task.CompletedTask;
    });
    [Fact]
    public Task ReplyLandsAfterAChip() => Run(win =>
    {
        var clock = new Steps(win); win.RunAskEffect = _ => { };
        win.ShowAsk(Ask());
        clock.Advance(1360);
        win.AnswerAsk(1);
        clock.Advance(1360); Assert.Equal("reply.no", win.MomentBubbleText);
        return Task.CompletedTask;
    });
    [Fact]
    public Task PresentationCancelsTheAsk() => Run(win =>
    {
        var clock = new Steps(win); win.ShowAsk(Ask());
        win.BeginPresentation(() => { });
        try
        {
            Assert.False(win.AskLive);
            clock.Advance(10000); Assert.DoesNotContain(win.BubbleHost.Children, c => c.IsVisible);
        }
        finally { win.EndPresentation(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task FullScreenMomentCancelsAskAndTeardownCancelsTimers() => Run(win =>
    {
        var clock = new Steps(win); win.ShowAsk(Ask());
        EmiDeskService.Instance.Fire("videoRunning");
        Assert.False(win.AskLive);
        win.StopMomentBubble();
        clock.Advance(1500); Assert.False(win.AskLive);
        Assert.DoesNotContain(win.BubbleHost.Children, c => c.IsVisible);
        return Task.CompletedTask;
    });
    [Fact]
    public Task PanicRegistryClearsTheRealServiceBubble() => Run(async unused =>
    {
        var service = EmiDeskService.Instance; var old = global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled;
        var oldMute = global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar;
        try
        {
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled = true;
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar = false;
            await service.Summon();
            service.Window!.BeginPresentation(() => { }); service.Window.EndPresentation();
            Assert.True(EmiLineEngine.AskSituationProbe!());
            var sessionProbe = global::ConditioningControlPanel.CoreSession.IsSessionRunningProvider;
            try
            {
                global::ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => true;
                Assert.False(EmiLineEngine.AskSituationProbe!());
            }
            finally { global::ConditioningControlPanel.CoreSession.IsSessionRunningProvider = sessionProbe; }
            string? moment = null;
            EventHandler<EmiMoment> heard = (_, value) => moment = value.Id;
            service.MomentFired += heard;
            try { service.Window!.ToggleRing(); Assert.Equal("ringOpen", moment); }
            finally { service.MomentFired -= heard; }
            var clock = new Steps(service.Window!);
            service.Window!.ShowAsk(Ask());
            PanicSurfaces.All.Single(s => s.Id == "emi-moment-bubble").Stop(null);
            Assert.False(service.Window.AskLive);
            clock.Advance(1500); Assert.DoesNotContain(service.Window.BubbleHost.Children, c => c.IsVisible);
        }
        finally
        {
            service.Window?.ShutDown();
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled = old;
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar = oldMute;
        }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewSpeechCancelsHeldFacesAndParkedDelivery(bool ask) => Run(win =>
    {
        var clock = new Steps(win); void NewSpeech() { if (ask) win.ShowAsk(Ask()); else win.SpeakLine(Line("fresh")); }
        win.SpeakLine(new LineDraw("hold", "test", "", "-_-", null, 2, true, 2000));
        NewSpeech(); clock.Advance(2000);
        Assert.Equal("^_^", win.Face);
        win.StopMomentBubble(); win.ShowAsk(Ask()); win.SpeakLine(Line("obsolete"));
        clock.Advance(1360); win.RunAskEffect = _ => { }; win.AnswerAsk(1);
        NewSpeech(); clock.Advance(2260);
        Assert.Equal(ask ? "a question?" : "fresh", win.MomentBubbleText);
        if (ask) { win.AnswerAsk(1); clock.Advance(2260); Assert.Equal("reply.no", win.MomentBubbleText); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task SpeechOwnsTheFaceUntilCompletionOrCancellation() => Run(win =>
    {
        var clock = new Steps(win);
        void Pat(string method) => typeof(EmiDeskWindow).GetMethod(method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(win, method == "PetFromClick" ? null : new object?[] { null, EventArgs.Empty });
        Pat("PetFromClick"); Assert.True(win.ChainLive);
        win.Say("speech", "-_-"); win.AdvancePatFeedback(); Assert.Equal("0_0", win.Face);
        clock.Advance(1360); win.AdvancePatFeedback(); Pat("PetFromClick"); Pat("OnPetTick"); Assert.Equal("-_-", win.Face); Assert.True(win.ChainLive);
        clock.Advance(EmiChains.SayHoldMs("speech")); Assert.False(win.ChainLive);
        win.Say("again"); win.CancelChain(); Assert.False(win.ChainLive);
        clock.Advance(10000); Assert.Null(win.MomentBubbleText);
        return Task.CompletedTask;
    });
    [Fact]
    public Task BubbleMeasuresItsCrownAndDodgesEdgesAndTheBook() => Run(win =>
    {
        var clock = new Steps(win); win.ShowAsk(Ask() with { Question = new string('w', 70) }); clock.Advance(1360); Dispatcher.UIThread.RunJobs(); var bubble = Assert.IsType<StackPanel>(Assert.Single(win.BubbleHost.Children));
        var screen = win.Screens.ScreenFromWindow(win)!;
        win.BubbleClientPoint = p => new Point((p.X - win.Position.X) / win.RenderScaling, (p.Y - win.Position.Y) / win.RenderScaling);
        void Place(double bodyRight) => win.Position = new PixelPoint((int)(bodyRight -
            (EmiDeskWindow.OverlayPadX + win.BodyWidth) * win.RenderScaling), screen.WorkingArea.Y + 200);
        Place(screen.WorkingArea.X + screen.WorkingArea.Width / 2);
        win.LayoutSpeech(); Assert.Equal(EmiDeskWindow.OverlayPad + win.BodyWidth * EmiDeskWindow.BodyAspect * .04,
            Canvas.GetTop(bubble) + bubble.Children[0].DesiredSize.Height - 10, 0);
        win.OpenBook(); Assert.Equal(1, win.Book!.SideOfHer); Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Right, win.MomentBubbleTail!.HorizontalAlignment);
        win.CloseBook(); Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Left, win.MomentBubbleTail.HorizontalAlignment);
        Place(screen.WorkingArea.Right); win.LayoutSpeech();
        Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Right, win.MomentBubbleTail.HorizontalAlignment);
        Assert.True(Canvas.GetLeft(bubble) + bubble.DesiredSize.Width <= win.BubbleClientPoint(screen.WorkingArea.BottomRight).X);
        Place(screen.WorkingArea.Right - 260 * win.RenderScaling); win.OpenBook();
        Assert.Equal(-1, win.Book!.SideOfHer); Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Left, win.MomentBubbleTail.HorizontalAlignment);
        win.ApplyBodyWidth(300); Assert.Equal(15, win.MomentBubbleTextBlock!.FontSize);
        return Task.CompletedTask;
    });
}
