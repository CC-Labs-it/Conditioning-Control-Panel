using System;
using System.Linq;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.EmiDesk;
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
    public Task SayUsesTheLockedDotCadenceAndCloseCutsTheBubble() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            desk.Say("a card stays when you pin it.");
            Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == ".");
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "a card stays when you pin it.")
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(40);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Contains(desk.BubbleHost.GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Text == "a card stays when you pin it.");
            if (Environment.GetEnvironmentVariable("CCP_EMI_RING_RENDER_PATH") is { Length: > 0 } output)
                desk.CaptureRenderedFrame()!.Save(output);
            desk.CancelChain();
            Assert.DoesNotContain(desk.BubbleHost.Children.OfType<Border>(), b => b.IsVisible);
        }
        finally { desk.ShutDown(); }
    });
    private static async Task WaitUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done());
    }

    [Fact]
    public Task HoldCutsAPlayingLineAndReturnsToRestWithoutSpendingSpeech() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            await WaitUntil(() => desk.Face == "0_0");
        }
        finally { desk.ShutDown(); }
    });

    [Fact]
    public Task RealRingEmitsOpenDismissAndThirdIgnoreAndFirstPinRetiresOnboarding() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = EmiDeskService.Instance;
        var oldEngine = service.LineEngine;
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
            service.MomentFired += observer;
            await service.Summon("test");
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            await WaitUntil(() => !desk.Transiting);
            for (int i = 0; i < 3; i++)
            {
                desk.ToggleRing(); Assert.True(desk.RingOpen);
                desk.CloseRing();
                await WaitUntil(() => !desk.RingOpen);
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
            (CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.EmiDeskMuteAvatar,
                st.RingIgnoreStreak, st.RingOpens, st.PinGistGot) = old;
        }
    });
    [Fact]
    public Task DelayedPinNudgeSpendsOneFireAndDismissalCancelsThePendingAttempt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new EmiDeskService { LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}") };
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
            await WaitUntil(() => !desk.Transiting);
            service.NoteRingOpened();
            Assert.False(desk.ChainLive); // the 900 ms delay leaves the new cards unobscured
            await WaitUntil(() => st.NudgeFires.ContainsKey(EmiNudgeMachine.PinTrack));
            Assert.Equal(1, st.NudgeFires[EmiNudgeMachine.PinTrack]);
            Assert.True(desk.ChainLive);
            desk.CancelChain();
            service.NoteRingOpened();
            await Task.Delay(1100); Dispatcher.UIThread.RunJobs();
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
        var pending = new EmiDeskService { LineEngine = EmiLineEngine.FromJson("{\"moments\":{},\"pools\":{}}") };
        try
        {
            CoreSettings.Current.EmiDeskEnabled = true; CoreSettings.Current.EmiDeskMuteAvatar = false;
            st.PinGistGot = false; st.NudgeFires.Remove(EmiNudgeMachine.PinTrack);
            await pending.Summon("pending-test");
            var desk = Assert.IsType<EmiDeskWindow>(pending.Window);
            await WaitUntil(() => !desk.Transiting);
            pending.NoteRingOpened(); pending.Dismiss();
            await Task.Delay(1100); Dispatcher.UIThread.RunJobs();
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
