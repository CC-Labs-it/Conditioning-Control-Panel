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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var win = new EmiDeskWindow();
        try { win.Show(); await test(win); }
        finally { win.ShutDown(); Dispatcher.UIThread.RunJobs(); }
    });

    /// <summary>Stepped clock for the window's one-shot timers (P08): Advance fires due ticks in order.</summary>
    private sealed class Steps
    {
        private sealed class Entry { public long Due; public Action Tick = () => { }; public bool Stopped; }
        private readonly List<Entry> _pending = new();
        private long _now;
        public Steps(EmiDeskWindow win) => win.StartTimer = Start;
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
        var clock = new Steps(win);
        string? effect = null;
        win.RunAskEffect = value => { Assert.False(win.AskLive); effect = value; };
        win.ShowAsk(Ask());
        Assert.Equal(".", win.MomentBubbleText);
        win.SpeakLine(Line("older"));
        win.SpeakLine(Line("newest"));
        Assert.Equal(".", win.MomentBubbleText);
        clock.Advance(1360);
        Assert.Equal("a question?", win.MomentBubbleText);
        win.SpeakLine(new LineDraw("hold", "test", "", "-_-", null, 2, true, 500));
        clock.Advance(1600);
        Assert.True(win.AskLive);
        Assert.Equal("a question?", win.MomentBubbleText);
        win.AnswerAsk(0);
        Assert.False(win.AskLive);
        Assert.Equal("book:open", effect);
        Assert.Equal(".", win.MomentBubbleText);
        clock.Advance(420);
        Assert.Equal("..", win.MomentBubbleText);
        clock.Advance(480 + 1360);
        Assert.Equal("newest", win.MomentBubbleText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplyLandsAfterAChip() => Run(win =>
    {
        var clock = new Steps(win);
        win.RunAskEffect = _ => { };
        win.ShowAsk(Ask());
        clock.Advance(1360);
        win.AnswerAsk(1);
        clock.Advance(1360);
        Assert.Equal("reply.no", win.MomentBubbleText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PresentationCancelsTheAsk() => Run(win =>
    {
        new Steps(win);
        win.ShowAsk(Ask());
        win.BeginPresentation(() => { });
        try { Assert.False(win.AskLive); }
        finally { win.EndPresentation(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullScreenMomentCancelsAskAndTeardownCancelsTimers() => Run(win =>
    {
        var clock = new Steps(win);
        win.ShowAsk(Ask());
        EmiDeskService.Instance.Fire("videoRunning");
        Assert.False(win.AskLive);
        win.StopMomentBubble();
        clock.Advance(1500);
        Assert.False(win.AskLive);
        Assert.DoesNotContain(win.BubbleHost.Children, c => c.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicRegistryClearsTheRealServiceBubble() => Run(async unused =>
    {
        var service = EmiDeskService.Instance;
        var old = global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled;
        var oldMute = global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar;
        try
        {
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled = true;
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar = false;
            await service.Summon();
            string? moment = null;
            EventHandler<EmiMoment> heard = (_, value) => moment = value.Id;
            service.MomentFired += heard;
            try { service.Window!.ToggleRing(); Assert.Equal("ringOpen", moment); }
            finally { service.MomentFired -= heard; }
            var clock = new Steps(service.Window!);
            service.Window!.ShowAsk(Ask());
            PanicSurfaces.All.Single(s => s.Id == "emi-moment-bubble").Stop(null);
            Assert.False(service.Window.AskLive);
            clock.Advance(1500);
            Assert.DoesNotContain(service.Window.BubbleHost.Children, c => c.IsVisible);
        }
        finally
        {
            service.Window?.ShutDown();
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskEnabled = old;
            global::ConditioningControlPanel.CoreSettings.Current.EmiDeskMuteAvatar = oldMute;
        }
    });
}
