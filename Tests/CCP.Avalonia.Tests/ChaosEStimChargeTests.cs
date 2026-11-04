using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosEStimChargeTests
{
    private static AmbientBubble Bubble(double x, bool live = false) => new()
    {
        X = x, Y = 0, Size = 20, Clickable = true,
        Spec = new EffectBubbleSpec { IsLive = live }
    };

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        BubbleOverlay.Field.Bubbles.Clear();
        BubbleOverlay.BeginChaos(new Canvas(), _ => { }, _ => { });
    }

    [Fact]
    public void Toy_key_and_button_honor_gate_cooldown_and_multiplier() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldCanShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.CanShow = () => false;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            ChaosMeta.Init();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            var toy = new ChaosToyState { Id = "e_stim", KeyLabel = "Q", CooldownSec = 10 };
            state.ActiveToys.Clear(); state.ActiveToys.Add(toy);
            state.ToyPower["e_stim"] = 3; state.EStimChargeMult = 2;
            state.ActivesDisabled = true;
            ChaosRunHost.OnToyKey("Q");
            Assert.Equal(0, BubbleOverlay.EStimCharges);
            state.ActivesDisabled = false;
            ChaosRunHost.UseToyById("e_stim");
            Assert.Equal(6, BubbleOverlay.EStimCharges);
            Assert.Equal(10, toy.CooldownRemainingSec);
            state.EStimChargeMult = 1;
            ChaosRunHost.OnToyKey("Q");
            Assert.Equal(6, BubbleOverlay.EStimCharges);
            ChaosRunHost.ForceShutdown();
            Assert.Equal(0, BubbleOverlay.EStimCharges);
        }
        finally { ChaosRunHost.ForceShutdown(); shell.Close(); ChaosEStimOverlay.CanShow = oldCanShow; }
    });

    [Fact]
    public Task Player_click_spends_only_with_near_targets_and_pays_nearest() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldCanShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.CanShow = () => false;
        try
        {
            var source = Bubble(0); var far = Bubble(1000);
            BubbleOverlay.Field.Bubbles.AddRange(new[] { source, far });
            BubbleOverlay.ArmEStim(2, false);
            BubbleOverlay.Press(source, 0, 0);
            Assert.Equal(2, BubbleOverlay.EStimCharges);
            var next = Bubble(0); var a = Bubble(100); var b = Bubble(200, true);
            var c = Bubble(300); var d = Bubble(400);
            BubbleOverlay.Field.Bubbles.AddRange(new[] { next, a, b, c, d });
            BubbleOverlay.PopChaos(next); // a skill pop does not conduct
            Assert.Equal(2, BubbleOverlay.EStimCharges);
            next = Bubble(0); BubbleOverlay.Field.Bubbles.Add(next);
            BubbleOverlay.Press(next, 0, 0);
            Assert.Equal(1, BubbleOverlay.EStimCharges);
            await Task.Delay(350);
            Assert.True(a.Popping); Assert.True(b.Deflating); Assert.True(c.Popping);
            Assert.False(d.Popping); Assert.False(far.Popping);
        }
        finally { BubbleOverlay.EndChaos(); ChaosEStimOverlay.CloseActive(); ChaosEStimOverlay.CanShow = oldCanShow; }
    });

    [Fact]
    public Task Capstone_reaches_beyond_first_hop_and_cleanup_cancels_pending_hits() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldCanShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.CanShow = () => false;
        try
        {
            var source = Bubble(0); var first = Bubble(500); var second = Bubble(1000);
            BubbleOverlay.Field.Bubbles.AddRange(new[] { source, first, second });
            BubbleOverlay.ArmEStim(1, true);
            BubbleOverlay.Press(source, 0, 0);
            Assert.Equal(0, BubbleOverlay.EStimCharges);
            await Task.Delay(250);
            Assert.True(first.Popping); Assert.True(second.Popping);
            source = Bubble(0); first = Bubble(500);
            BubbleOverlay.Field.Bubbles.AddRange(new[] { source, first });
            BubbleOverlay.ArmEStim(1, false);
            BubbleOverlay.Press(source, 0, 0);
            BubbleOverlay.EndChaos();
            await Task.Delay(150);
            Assert.False(first.Popping);
            Assert.Equal(0, BubbleOverlay.EStimCharges);
        }
        finally { BubbleOverlay.EndChaos(); ChaosEStimOverlay.CloseActive(); ChaosEStimOverlay.CanShow = oldCanShow; }
    });
}
