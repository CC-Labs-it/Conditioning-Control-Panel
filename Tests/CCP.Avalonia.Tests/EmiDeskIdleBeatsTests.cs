using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskIdleBeatsTests
{
    [Fact]
    public Task SummonStartsBlinkAndSwayAndChainStopsThem() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var steps = new Queue<(int ms, Action run)>();
        var desk = new EmiDeskWindow();
        desk.AfterScheduler = (ms, run) => steps.Enqueue((ms, run));
        desk.IdleRoll = _ => 1;
        try
        {
            desk.RunSummon();
            while (steps.Count > 0) steps.Dequeue().run();
            while (desk.ChainLive) desk.ChainPlayer.ElapsePendingHold();
            Assert.True(desk.IdleBeatsRunning);
            desk.TickIdleForTest();
            Assert.Equal(EmiAlive.BlinkFace, desk.Face);
            var blink = steps.Dequeue();
            Assert.Equal(EmiAlive.BlinkHoldMs, blink.ms);
            blink.run();
            Assert.Equal(EmiChains.RestFace, desk.Face);
            foreach (var key in EmiChains.SwayCycle)
            {
                desk.TickSwayForTest();
                Assert.Equal(key, desk.IdlePoseForTest);
                if (key == "idle") Assert.InRange(desk.SwayIntervalForTest, 600, 900);
                else Assert.Equal(200, desk.SwayIntervalForTest);
            }
            desk.PlayChain("wake");
            Assert.False(desk.IdleBeatsRunning);
            desk.RestartIdleBeats(); // a queued tick must not take the live chain's face or pose
            var face = desk.Face;
            var pose = desk.IdlePoseForTest;
            desk.TickIdleForTest(); desk.TickSwayForTest();
            Assert.Equal(face, desk.Face);
            Assert.Equal(pose, desk.IdlePoseForTest);
            desk.StopChainForPanic();
            Assert.False(desk.IdleBeatsRunning);
            desk.RestartIdleBeats();
            desk.ShutDown();
            Assert.False(desk.IdleBeatsRunning);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DoubleBlinkAndGuardRespectFaceOwnership() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var steps = new Queue<(int ms, Action run)>();
        var desk = new EmiDeskWindow();
        desk.AfterScheduler = (ms, run) => steps.Enqueue((ms, run));
        desk.IdleRoll = _ => 0;
        try
        {
            desk.Show(); desk.RestartIdleBeats();
            desk.TickIdleForTest();
            Assert.Equal(EmiAlive.BlinkFace, desk.Face);
            steps.Dequeue().run();
            Assert.Equal(EmiChains.RestFace, desk.Face);
            var gap = steps.Dequeue();
            Assert.Equal(EmiAlive.DoubleBlinkGapMs, gap.ms);
            gap.run();
            Assert.Equal(EmiAlive.BlinkFace, desk.Face);
            steps.Dequeue().run();
            Assert.Equal(EmiChains.RestFace, desk.Face);
            desk.TickIdleForTest();
            desk.PlayChain("wake");
            var face = desk.Face;
            steps.Dequeue().run();
            Assert.Equal(face, desk.Face);
            desk.BeginPresentation(() => { });
            Assert.False(desk.IdleBeatsRunning);
            face = desk.Face;
            desk.TickIdleForTest();
            Assert.Equal(face, desk.Face);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task HeldFaceStopsClocksAndRejectsPendingBlink() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var steps = new Queue<(int ms, Action run)>();
        var desk = new EmiDeskWindow();
        desk.AfterScheduler = (ms, run) => steps.Enqueue((ms, run));
        desk.IdleRoll = _ => 1;
        try
        {
            desk.Show(); desk.RestartIdleBeats(); desk.TickIdleForTest();
            var blink = steps.Dequeue();
            var engine = EmiLineEngine.FromJson(EmiDeskChainWiringTests.QuietLines);
            desk.SpeakLine(new LineDraw("hold", "", "", "o_o", null, 3, true, 5000), engine);
            Assert.True(desk.HoldPending);
            Assert.False(desk.IdleBeatsRunning);
            blink.run();
            Assert.Equal("o_o", desk.Face);
            desk.RestartIdleBeats();
            desk.TickIdleForTest(); desk.TickSwayForTest();
            Assert.Equal("o_o", desk.Face);
            desk.ExpireHold();
            Assert.True(desk.IdleBeatsRunning);
            Assert.Equal(EmiChains.RestFace, desk.Face);
        }
        finally { desk.AfterScheduler = null; desk.ShutDown(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task GlassStopsClocksAndParksTicks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var bedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow();
        try
        {
            CoreSettings.Current.EmiDeskGlass = true;
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            desk.Show(); desk.RestartIdleBeats();
            desk.PollGlass(DateTime.UtcNow.AddSeconds(61));
            Assert.True(desk.GlassIsLive);
            Assert.False(desk.IdleBeatsRunning);
            desk.RestartIdleBeats();
            var face = desk.Face; var pose = desk.IdlePoseForTest;
            desk.TickIdleForTest(); desk.TickSwayForTest();
            Assert.Equal(face, desk.Face); Assert.Equal(pose, desk.IdlePoseForTest);
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime;
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

}
