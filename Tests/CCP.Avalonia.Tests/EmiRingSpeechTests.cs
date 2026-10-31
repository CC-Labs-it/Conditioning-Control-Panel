using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
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
    public Task SayUsesCoreCadenceAndCancellationDropsTheContinuation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            bool done = false;
            desk.Say("a card stays when you pin it.", done: () => done = true);
            foreach (var expected in new[] { ".", "..", "...", "a card stays when you pin it." })
            {
                Assert.True(desk.ChainLive);
                Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == expected);
                desk.StepSpeech();
            }
            desk.StepSpeech();
            Assert.True(done);
            Assert.False(desk.ChainLive);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
            done = false;
            desk.Say("cancelled", done: () => done = true);
            desk.CancelChain(); desk.StepSpeech();
            Assert.False(done);
            Assert.False(desk.ChainLive);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
            desk.Say("hidden"); desk.Hide();
            Assert.False(desk.ChainLive);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HoldCutsSpeechAndReturnsToRest() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show(); desk.Say("stop this");
            var engine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
            Assert.False(desk.SpeakRingLine(new LineDraw("h", "h", "", "-_-", null, 3, true, 100), engine));
            Assert.False(desk.ChainLive);
            Assert.Equal("-_-", desk.Face);
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
            desk.EndHold();
            Assert.Equal(EmiChains.RestFace, desk.Face);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RealRingFiresOpenDismissAndThirdIgnoreAndPanicCutsSpeech() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        service.Window?.ShutDown();
        CoreSettings.Current.EmiDeskEnabled = true;
        CoreSettings.Current.EmiDeskMuteAvatar = false;
        EmiState.Current.RingIgnoreStreak = 0;
        var oldEngine = service.LineEngine;
        service.LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}");
        var moments = new List<string>();
        EventHandler<EmiMoment> observer = (_, m) => moments.Add(m.Id);
        service.MomentFired += observer;
        try
        {
            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            for (int i = 0; i < 3; i++)
            {
                desk.ToggleRing(); Assert.True(desk.RingOpen);
                desk.CloseRing(); Assert.False(desk.RingOpen);
            }
            Assert.Equal(3, moments.Count(m => m == "ringOpen"));
            Assert.Equal(3, moments.Count(m => m == "ringDismissed"));
            Assert.Single(moments.Where(m => m == "suggestionIgnored3x"));
            Assert.Equal(0, EmiState.Current.RingIgnoreStreak);
            service.LineEngine = EmiLineEngine.FromJson("""
                {"moments":{"ringOpen":{"pools":["ring"],"priority":3,"cooldownMs":60000},
                "panicPressed":{"hold":true,"tailMs":300000}},
                "pools":{"ring":[{"id":"ring.1","t":"a card stays","face":"^_^","spice":0}]}}
                """);
            service.Fire("ringOpen");
            Assert.True(desk.ChainLive);
            Assert.Null(service.LineEngine.Draw("ringOpen")); // first dot acknowledged the cooldown
            desk.StepSpeech(); desk.StepSpeech(); desk.StepSpeech();
            Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "a card stays");
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "emi-speech").Stop(null);
            Assert.False(desk.ChainLive);
            Assert.Contains("panicPressed", moments);
            Assert.True(service.LineEngine.HoldActive);
            service.Window?.ShutDown();
            var away = new EmiDeskService { LineEngine = EmiLineEngine.FromJson("""
                {"moments":{"panicPressed":{"hold":true,"tailMs":300000}},"pools":{}}
                """) };
            away.Fire("panicPressed");
            Assert.True(away.LineEngine.HoldActive);
        }
        finally
        {
            service.MomentFired -= observer;
            service.Window?.ShutDown();
            service.LineEngine = oldEngine;
        }
    });
}
