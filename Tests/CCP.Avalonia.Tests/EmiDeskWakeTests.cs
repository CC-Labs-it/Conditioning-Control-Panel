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

public sealed class EmiDeskWakeTests
{
    // WPF EmiChainPlayer.cs:77-133: completion waits for the last hold, cancellation never completes.
    [Fact]
    public Task WakeUsesCanonHoldsAndCompletesOnce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        int completed = 0;
        try
        {
            desk.RunSummon(() => completed++);
            await Entrance(desk);
            Assert.Equal("-_-", desk.Face);
            Assert.Equal(0, completed);
            void Step(int ms, string face)
            {
                clock.Advance(ms); desk.AdvancePatFeedback();
                Assert.Equal(face, desk.Face);
                Assert.True(desk.ChainLive);
                Assert.Equal(0, completed);
            }
            Step(499, "-_-"); Step(1, "o_o");
            Step(219, "o_o"); Step(1, "0_0");
            Step(259, "0_0"); Step(1, "(⊙_⊙)");
            Step(699, "(⊙_⊙)");
            clock.Advance(1); desk.AdvancePatFeedback();
            Assert.False(desk.ChainLive);
            Assert.Equal(1, completed);
            clock.Advance(5000); desk.AdvancePatFeedback();
            Assert.Equal(1, completed);
            desk.PlayChain("wake", () => completed++);
            desk.CancelChain();
            clock.Advance(5000); desk.AdvancePatFeedback();
            Assert.Equal(1, completed);
            Assert.False(desk.ChainLive);
        }
        finally { desk.ShutDown(); }
    });

    // WPF React.cs:132-137: a pat cuts the entrance, finishes its bookkeeping, then pats normally.
    [Fact]
    public Task PatCutsWakeAndFinishesSummonExactlyOnce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var clock = new ManualClock();
        var desk = new EmiDeskWindow { PatClock = clock };
        int completed = 0;
        int pets = EmiState.Current.PetsTotal;
        bool gist = EmiState.Current.PetGistGot;
        try
        {
            desk.RunSummon(() => completed++);
            await Entrance(desk);
            typeof(EmiDeskWindow).GetMethod("PetFromClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desk, null);
            Assert.Equal(1, completed);
            Assert.Equal("^_^", desk.Face);
            Assert.Equal(pets + 1, EmiState.Current.PetsTotal);
            clock.Advance(5000); desk.AdvancePatFeedback();
            desk.FinishSummon();
            Assert.Equal(1, completed);
            Assert.False(desk.ChainLive);
        }
        finally
        {
            desk.ShutDown();
            EmiState.Current.PetsTotal = pets;
            EmiState.Current.PetGistGot = gist;
        }
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static async Task Entrance(EmiDeskWindow desk)
    {
        for (int t = 0; t < 3000 && (desk.InputLocked || !desk.ChainLive); t += 20)
        {
            await Task.Delay(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(desk.InputLocked);
        Assert.True(desk.ChainLive);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int ms) => _now = _now.AddMilliseconds(ms);
    }
}
