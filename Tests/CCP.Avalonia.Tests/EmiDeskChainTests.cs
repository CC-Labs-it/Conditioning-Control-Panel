using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskChainTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await body();
    });

    [Theory]
    [InlineData("wake")]
    [InlineData("wink")]
    [InlineData("dizzy")]
    public Task WordlessChainShowsEveryFrameAndFinishesAfterTheLastHold(string id) => Run(() =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            int done = 0;
            var chain = EmiChains.Get(id)!;
            desk.PlayChain(id, () => done++);
            foreach (var frame in chain.Seq)
            {
                Assert.True(desk.ChainLive);
                Assert.Equal(frame.Text, desk.Face);
                Assert.Equal(frame.Ms, desk.ChainInterval);
                Assert.Equal(0, done);
                desk.AdvanceChain();
            }
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            Assert.Equal(1, done);
            desk.AdvanceChain();
            Assert.Equal(1, done);
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SayUsesTheTourBubbleAndCompletesOnlyAfterTheFinalHold() => Run(() =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            int done = 0;
            desk.Say("hello", "^_^", () => done++);
            foreach (var frame in EmiChains.MakeSay("hello", "^_^", EmiChains.SayHoldMs("hello")).Seq)
            {
                Assert.True(desk.ChainLive);
                Assert.Equal(frame.Bubble, desk.TourSpeechText);
                Assert.Equal(frame.Ms, desk.TourSpeechInterval);
                Assert.Equal(0, done);
                desk.AdvanceTourSpeech();
            }
            Assert.False(desk.ChainLive);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            Assert.Equal(1, done);
            desk.Say("cancelled", done: () => done++);
            desk.CancelChain();
            desk.AdvanceTourSpeech();
            Assert.Null(desk.TourSpeechText);
            Assert.Equal(1, done);
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplacementAndCancellationDoNotRunOldContinuations() => Run(() =>
    {
        var desk = new EmiDeskWindow();
        int done = 0;
        try
        {
            desk.Show();
            desk.PlayChain("wake", () => done++);
            desk.PlayChain("missing", () => done += 100);
            Assert.Equal("-_-", desk.Face);
            desk.PlayChain("wink", () => done += 10);
            Assert.Equal("^_^", desk.Face);
            for (int i = 0; i < EmiChains.Get("wink")!.Seq.Count; i++) desk.AdvanceChain();
            Assert.Equal(10, done);
            desk.PlayChain("wake", () => done++);
            desk.CancelChain();
            desk.AdvanceChain();
            Assert.False(desk.ChainLive);
            Assert.Equal(10, done);
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ChainsAndSpeechReplaceOneAnother() => Run(() =>
    {
        var desk = new EmiDeskWindow();
        int done = 0;
        try
        {
            desk.Show();
            desk.Say("first", done: () => done++);
            desk.PlayChain("wake", () => done++);
            Assert.Null(desk.TourSpeechText);
            Assert.Equal("-_-", desk.Face);
            desk.Say("again", done: () => done++);
            Assert.Equal(".", desk.TourSpeechText);
            desk.AdvanceChain();
            Assert.Equal(".", desk.TourSpeechText);
            desk.CancelChain();
            Assert.Equal(0, done);
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClosingCancelsTheRunningChain() => Run(() =>
    {
        var desk = new EmiDeskWindow();
        int done = 0;
        desk.Show();
        desk.PlayChain("wake", () => done++);
        desk.Close();
        desk.AdvanceChain();
        Assert.False(desk.ChainLive);
        Assert.Equal(0, done);
        return Task.CompletedTask;
    });

    [Fact]
    public Task APatCutsOnlyTheWakeAndCompletesTheSummonOnce() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            int done = 0;
            desk.RunSummon(() => done++);
            await PumpUntil(() => desk.ChainLive && !desk.Transiting);
            Assert.Equal("-_-", desk.Face);
            var pat = typeof(EmiDeskWindow).GetMethod("PetFromClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
            pat.Invoke(desk, null);
            Assert.Equal(1, done);
            Assert.NotEqual("-_-", desk.Face);
            desk.FinishSummon();
            Assert.Equal(1, done);
        }
        finally { desk.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DismissDuringStartupCannotBeOvertakenByWake(bool afterReveal) => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            int summoned = 0, dismissed = 0;
            desk.RunSummon(() => summoned++);
            var body = desk.FindControl<Control>("BodyRoot")!;
            var crt = (ScaleTransform)typeof(EmiDeskWindow).GetField("_crtScale", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
            Assert.False(body.IsVisible);
            if (afterReveal) await PumpUntil(() => body.IsVisible);
            desk.RunDismiss(() => dismissed++);
            Assert.True(desk.IsVisible);
            Assert.True(body.IsVisible);
            Assert.Equal(1, crt.ScaleX);
            Assert.Equal(1, crt.ScaleY);
            Assert.Equal("^_^", desk.Face);
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsVisible);
            Assert.Equal(1, crt.ScaleY);
            await PumpUntil(() => dismissed == 1);
            Assert.False(desk.IsVisible);
            Assert.False(desk.ChainLive);
            Assert.Equal(0, summoned);
        }
        finally { desk.Close(); }
    });

    [Fact]
    public Task DismissWaitsForTheWinkBeforeTheOutro() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            int dismissed = 0;
            desk.RunDismiss(() => dismissed++);
            foreach (var frame in EmiChains.Get("wink")!.Seq)
            {
                Assert.True(desk.IsVisible);
                Assert.Equal(frame.Text, desk.Face);
                Assert.Equal(frame.Ms, desk.ChainInterval);
                Assert.Equal(0, dismissed);
                desk.AdvanceChain();
            }
            Assert.True(desk.IsVisible);
            await PumpUntil(() => dismissed == 1);
            Assert.False(desk.IsVisible);
        }
        finally { desk.Close(); }
    });

    [Fact]
    public Task PanicDuringDismissKeepsTheOutroAndClearsTheService() => Run(async () =>
    {
        var previous = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        var service = EmiDeskService.Instance;
        try
        {
            settings.Current.EmiDeskEnabled = true;
            settings.Current.EmiDeskMuteAvatar = false;
            await service.Summon("test");
            var desk = service.Window!;
            Assert.True(service.IsOut);
            desk.Say("A line");
            Assert.Equal(".", desk.TourSpeechText);
            PanicSurfaces.All.Single(surface => surface.Id == "emi-desk-feedback").Stop(null);
            Assert.Null(desk.TourSpeechText);
            Assert.False(desk.ChainLive);
            service.Dismiss();
            Assert.True(desk.ChainLive);
            Assert.True(desk.InputLocked);
            PanicSurfaces.StopAll("test");
            Assert.True(desk.ChainLive);
            await PumpUntil(() => !service.IsOut);
            Assert.False(desk.IsVisible);
            Assert.False(desk.InputLocked);
        }
        finally
        {
            service.Window?.ShutDown();
            ConditioningControlPanel.CoreSettings.ServiceProvider = previous;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            EmiLineEngine.Instance.ResetForTests();
        }
    });

    [Fact]
    public Task RightClickDuringDizzyKeepsTheChainAndOpensTheRing() => Run(() =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            desk.Show();
            int done = 0;
            desk.PlayChain("dizzy", () => done++);
            RightClickBody(desk);
            Assert.True(desk.RingOpen);
            Assert.True(desk.ChainLive);
            Assert.Equal("@_@", desk.Face);
            for (int i = 0; i < EmiChains.Get("dizzy")!.Seq.Count; i++) desk.AdvanceChain();
            Assert.Equal(1, done);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RightClickDuringWakeStillFinishesTheSummonAndOpensTheRing() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            int done = 0;
            desk.RunSummon(() => done++);
            await PumpUntil(() => desk.ChainLive && !desk.Transiting);
            RightClickBody(desk);
            Assert.True(desk.RingOpen);
            Assert.False(desk.ChainLive);
            Assert.Equal(1, done);
        }
        finally { desk.ShutDown(); }
    });

    [Theory]
    [InlineData("tour")]
    [InlineData("speech")]
    [InlineData("chain")]
    public Task ReplacementDuringDismissKeepsTheWink(string source) => Run(async () =>
    {
        var previous = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        var service = EmiDeskService.Instance;
        var state = EmiState.Current;
        var (summons, boot) = (state.SummonCount, state.FirstBootSeen);
        var seen = state.SeenByPool;
        bool hadSeen = seen.TryGetValue("tourStarted", out var rows);
        var before = rows?.ToArray() ?? Array.Empty<string>();
        EmiLineEngine.Instance.ResetForTests();
        try
        {
            settings.Current.EmiDeskEnabled = true;
            settings.Current.EmiDeskMuteAvatar = false;
            await service.Summon("test");
            var desk = service.Window!;
            service.Dismiss();
            ((DispatcherTimer)typeof(EmiDeskWindow).GetField("_chainTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!).Stop();
            if (source == "tour")
            {
                var tutorial = new TutorialService();
                using var narrator = new EmiTourNarrator(tutorial, service.SpeakTourMoment, _ => false);
                tutorial.Start(TutorialType.GettingStarted);
                Assert.True(tutorial.IsActive);
            }
            else if (source == "speech") desk.Say("A line");
            else desk.PlayChain("dizzy");
            Assert.Null(desk.TourSpeechText);
            Assert.True(desk.ChainLive);
            Assert.Equal("^_^", desk.Face);
            Assert.Equal(before, seen.TryGetValue("tourStarted", out var after) ? after.ToArray() : Array.Empty<string>());
            for (int i = 0; i < EmiChains.Get("wink")!.Seq.Count; i++) desk.AdvanceChain();
            await PumpUntil(() => !service.IsOut);
            Assert.False(desk.IsVisible);
            Assert.False(desk.InputLocked);
        }
        finally
        {
            if (hadSeen) seen["tourStarted"] = before.ToList(); else seen.Remove("tourStarted");
            service.Window?.ShutDown(); state.SummonCount = summons; state.FirstBootSeen = boot;
            ConditioningControlPanel.CoreSettings.ServiceProvider = previous;
            EmiLineEngine.Instance.ResetForTests();
        }
    });

    [Fact]
    public Task PanicDuringWakeKeepsItsCompletionAndLaterSpeech() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        var service = EmiDeskService.Instance;
        var field = typeof(EmiDeskService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = field.GetValue(service); var state = EmiState.Current;
        var (opens, streak, gist) = (state.RingOpens, state.RingIgnoreStreak, state.RingGistGot);
        try
        {
            field.SetValue(service, desk);
            int done = 0;
            desk.RunSummon(() => done++);
            await PumpUntil(() => desk.ChainLive && !desk.Transiting);
            ((DispatcherTimer)typeof(EmiDeskWindow).GetField("_chainTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!).Stop();
            PanicSurfaces.StopAll("test");
            Assert.True(desk.ChainLive);
            for (int i = 0; i < EmiChains.Get("wake")!.Seq.Count; i++) desk.AdvanceChain();
            Assert.Equal(1, done);
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.RunSummon(() => done++);
            await PumpUntil(() => desk.ChainLive && !desk.Transiting);
            desk.Say("A line");
            Assert.Equal(2, done);
            Assert.Equal(".", desk.TourSpeechText);
            ((DispatcherTimer)typeof(EmiDeskWindow).GetField("_tourSpeechTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!).Stop();
            RightClickBody(desk);
            Assert.True(desk.RingOpen);
            Assert.Equal(".", desk.TourSpeechText);
        }
        finally
        {
            field.SetValue(service, previous);
            desk.ShutDown(); state.RingOpens = opens; state.RingIgnoreStreak = streak; state.RingGistGot = gist;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            EmiLineEngine.Instance.ResetForTests();
        }
    });

    private static void RightClickBody(EmiDeskWindow desk)
    {
        // Freeze the live frame while the headless ring is built; the click still goes through the real handler.
        ((DispatcherTimer)typeof(EmiDeskWindow).GetField("_chainTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!).Stop();
        Dispatcher.UIThread.RunJobs();
        var body = desk.FindControl<Control>("BodyRoot")!;
        var point = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
        desk.MouseDown(point, MouseButton.Right);
        desk.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task EndingPresentationCancelsItsPendingEntrance() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            desk.BeginPresentation(() => { });
            desk.RunPresentationEntrance();
            Assert.True(desk.Transiting);
            desk.EndPresentation();
            await Task.Delay(900);
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.ChainLive);
            Assert.False(desk.Transiting);
            Assert.Equal("^_^", desk.Face);
        }
        finally { desk.Close(); }
    });

    private static async Task PumpUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "Timed out waiting for the desk transition");
    }
}
