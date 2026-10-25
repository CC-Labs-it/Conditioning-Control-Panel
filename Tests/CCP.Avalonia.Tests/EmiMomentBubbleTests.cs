using System;
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

    private static async Task Pump(int milliseconds)
    {
        for (int t = 0; t < milliseconds; t += 20)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public Task AskTypesThenWaitsAndParksSpeechUntilAChip() => Run(async win =>
    {
        string? effect = null;
        win.RunAskEffect = value => { Assert.False(win.AskLive); effect = value; };
        win.ShowAsk(Ask());
        Assert.Equal(".", win.MomentBubbleText);
        win.SpeakLine(Line("parked"));
        win.SpeakLine(Line("dropped"));
        Assert.Equal(".", win.MomentBubbleText);
        await Pump(1500);
        Assert.Equal("a question?", win.MomentBubbleText);
        Assert.True(win.AskLive);
        await Pump(1600);
        Assert.True(win.AskLive);
        win.AnswerAsk(0);
        Assert.False(win.AskLive);
        Assert.Equal("book:open", effect);
        await Pump(2400);
        Assert.Equal("parked", win.MomentBubbleText);
    });

    [Fact]
    public Task FullScreenMomentCancelsAskAndTeardownCancelsTimers() => Run(async win =>
    {
        win.ShowAsk(Ask());
        EmiDeskService.Instance.Fire("videoRunning");
        Assert.False(win.AskLive);
        win.StopMomentBubble();
        await Pump(1500);
        Assert.False(win.AskLive);
        Assert.DoesNotContain(win.BubbleHost.Children, c => c.IsVisible);
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
            service.Window!.ShowAsk(Ask());
            PanicSurfaces.All.Single(s => s.Id == "emi-moment-bubble").Stop(null);
            Assert.False(service.Window.AskLive);
            await Pump(1500);
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
