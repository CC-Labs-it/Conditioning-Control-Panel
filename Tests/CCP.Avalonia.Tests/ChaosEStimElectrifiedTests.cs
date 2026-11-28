using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class ChaosEStimElectrifiedTests
{
    private static AmbientBubble Bubble(double center, EffectBubbleSpec spec) => new()
    {
        X = center - 10, Y = -10, Size = 20, Clickable = true, Spec = spec
    };

    [Fact]
    public Task Victim_gate_selects_nearest_without_spending_or_chaining_and_end_cancels() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var stateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
        var zapField = typeof(ChaosRunHost).GetField("_lastBodyBuzzZap", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldState = stateField.GetValue(null);
        var oldZap = zapField.GetValue(null);
        var canShow = ChaosEStimOverlay.CanShow;
        var checks = ChaosEStimOverlay.SkipPlatformChecksForTests;
        var skia = CoreSettings.Current.ChaosSkiaFxEnabled;
        try
        {
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            ChaosEStimOverlay.CanShow = () => true;
            ChaosEStimOverlay.SkipPlatformChecksForTests = true;
            BubbleOverlay.BeginChaos(new Border(), _ => { }, _ => { });
            var state = new ChaosRunState(new ChaosRunConfig());
            stateField.SetValue(null, state);
            var live = Bubble(100, new EffectBubbleSpec { IsLive = true });
            var treat = Bubble(300, new EffectBubbleSpec());
            var edge = Bubble(620, new EffectBubbleSpec());
            var fourth = Bubble(615, new EffectBubbleSpec());
            var outside = Bubble(621, new EffectBubbleSpec());
            var darter = Bubble(10, new EffectBubbleSpec { IsDarter = true });
            var freeze = Bubble(20, new EffectBubbleSpec { IsFreeze = true });
            BubbleOverlay.Field.Bubbles.AddRange(new[] { edge, outside, fourth, freeze, treat, darter, live });
            BubbleOverlay.ArmEStim(2, true);
            ChaosRunHost.ElectrifiedVictim(new Point(0, 0));
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Equal(2, BubbleOverlay.EStimCharges);

            // The third closest eligible target is inside the boundary; the fourth is at 620.
            state.ElectrifiedRabbits = true;
            ChaosRunHost.ElectrifiedVictim(new Point(0, 0));
            Assert.Equal(3, ChaosRunHost.BodyBuzzHopCount);
            Assert.Equal(3, ChaosEStimOverlay.Active!.BoltCount);
            Assert.Equal(2, BubbleOverlay.EStimCharges);
            await Task.Delay(350);
            Assert.True(live.Popping && !live.Deflating);
            Assert.True(treat.Popping);
            Assert.True(fourth.Popping);
            Assert.False(edge.Popping);
            Assert.False(outside.Popping);
            Assert.False(darter.Popping);
            Assert.False(freeze.Popping);
            Assert.Equal(2, BubbleOverlay.EStimCharges);
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);

            // The inclusive 620 px boundary is reachable once nearer targets are gone.
            ChaosRunHost.ElectrifiedVictim(new Point(0, 0));
            Assert.Equal(1, ChaosRunHost.BodyBuzzHopCount);
            Assert.Equal(1, ChaosEStimOverlay.Active!.BoltCount);
            BubbleOverlay.EndChaos();
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);
            await Task.Delay(120);
            Assert.False(edge.Popping);
        }
        finally
        {
            BubbleOverlay.EndChaos();
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = checks;
            CoreSettings.Current.ChaosSkiaFxEnabled = skia;
            stateField.SetValue(null, oldState);
            zapField.SetValue(null, oldZap);
        }
    });
    [Fact]
    public Task Panic_cancels_pending_victim_hops() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var canShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.CanShow = () => false;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            ChaosMeta.Init();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.ElectrifiedRabbits = true;
            BubbleOverlay.Field.Bubbles.Clear();
            var target = Bubble(100, new EffectBubbleSpec());
            BubbleOverlay.Field.Bubbles.Add(target);
            ChaosRunHost.ElectrifiedVictim(new Point(0, 0));
            Assert.Equal(1, ChaosRunHost.BodyBuzzHopCount);
            ChaosRunHost.ForceShutdown();
            Assert.Equal(0, ChaosRunHost.BodyBuzzHopCount);
            await Task.Delay(120);
            Assert.False(target.Popping);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            shell.Close();
            ChaosEStimOverlay.CanShow = canShow;
        }
    });

}
