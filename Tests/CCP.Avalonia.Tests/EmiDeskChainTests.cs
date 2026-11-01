using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
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

    [Fact]
    public Task DismissDuringStartupCannotBeOvertakenByWake() => Run(async () =>
    {
        var desk = new EmiDeskWindow();
        try
        {
            int summoned = 0, dismissed = 0;
            desk.RunSummon(() => summoned++);
            desk.RunDismiss(() => dismissed++);
            Assert.True(desk.IsVisible);
            Assert.Equal("^_^", desk.Face);
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
