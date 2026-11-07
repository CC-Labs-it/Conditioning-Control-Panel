using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosSizeQueenTests
{
    // WPF ChaosModeService.cs:2035-2036; BubbleService.cs:1704-1719.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snap_draws_a_ring_and_sweeps_only_treats_at_the_expanding_front(bool useSkia)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var stateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
            var savedState = stateField.GetValue(null);
            var now = ChaosFieldFxOverlay.NowMs;
            var settings = CoreSettings.Current;
            var enabled = settings.ChaosSkiaFxEnabled;
            var skiaChecks = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
            var composited = ChaosSkiaFxOverlay.Composited;
            int paid = 0;
            try
            {
                var state = new ChaosRunState(new ChaosRunConfig()) { RippleEnabled = true };
                stateField.SetValue(null, state);
                settings.ChaosSkiaFxEnabled = useSkia;
                ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
                ChaosSkiaFxOverlay.Composited = () => true;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.NowMs = () => 0;
                BubbleOverlay.BeginChaos(new Border(), _ => paid++, ChaosRunHost.OnDefused);
                AmbientBubble Add(double x, EffectBubbleSpec? spec)
                {
                    var b = new AmbientBubble { X = x - 10, Y = -10, Size = 20, Spec = spec };
                    BubbleOverlay.Field.Bubbles.Add(b);
                    return b;
                }
                var snap = Add(0, new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
                snap.FuseRemainingMs = 5000;
                var near = Add(24, new EffectBubbleSpec());
                var far = Add(400, new EffectBubbleSpec());
                var outside = Add(431, new EffectBubbleSpec());
                var live = Add(10, new EffectBubbleSpec { IsLive = true });
                var rabbit = Add(10, new EffectBubbleSpec { IsDarter = true });
                var freeze = Add(10, new EffectBubbleSpec { IsFreeze = true });
                var ambient = Add(10, null);
                var tease = Add(10, new EffectBubbleSpec { IsTease = true });
                var brittle = Add(10, new EffectBubbleSpec { IsBrittle = true });
                Assert.True(snap.Snap());
                BubbleOverlay.Field.ChaosDefused!(snap);
                // WPF BubbleService.cs:1676-1677 selects exactly one renderer.
                if (useSkia)
                {
                    var fx = Assert.IsType<ChaosSkiaFxOverlay>(ChaosSkiaFxOverlay.Active);
                    Assert.True(fx.IsVisible && fx.IsRendering);
                    Assert.Equal(1, fx.Fx.RippleCount);
                    Assert.Equal(7, fx.Fx.ParticleCount); // ordinary ripple, not the strong cast
                    Assert.Null(ChaosFieldFxOverlay.Active);
                }
                else
                {
                    var fx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                    Assert.True(fx.IsVisible && fx.Clock.IsEnabled);
                    Assert.Null(ChaosSkiaFxOverlay.Active);
                }
                Assert.Equal(0, paid);
                BubbleOverlay.Field.HoldChaos = true;
                for (int i = 0; i < 20; i++) BubbleOverlay.TickChaosRipples();
                Assert.False(near.Popping);
                BubbleOverlay.Field.HoldChaos = false;
                BubbleOverlay.TickChaosRipples(); // 430 * 32 / 550 = 25 physical px
                Assert.True(near.Popping);
                Assert.False(far.Popping);
                for (int i = 0; i < 17; i++) BubbleOverlay.TickChaosRipples();
                Assert.True(far.Popping);
                Assert.Equal(2, paid);
                foreach (var b in new[] { outside, live, rabbit, freeze, ambient, tease, brittle }) Assert.False(b.Popping);
                var late = Add(10, new EffectBubbleSpec());
                BubbleOverlay.TickChaosRipples();
                Assert.False(late.Popping); // expired, no ghost wave
                BubbleOverlay.TriggerChaosRipple(snap);
                BubbleOverlay.EndChaos();
                BubbleOverlay.BeginChaos(new Border(), _ => paid++);
                var nextRun = Add(10, new EffectBubbleSpec());
                BubbleOverlay.TickChaosRipples();
                Assert.False(nextRun.Popping); // teardown cleared the wave
                BubbleOverlay.EndChaos();
                ChaosFieldFxOverlay.CloseActive();
                ChaosSkiaFxOverlay.CloseActive();
                BubbleOverlay.TriggerChaosRipple(snap);
                Assert.Null(ChaosFieldFxOverlay.Active); // no producer outside a run
                Assert.Null(ChaosSkiaFxOverlay.Active);
                state.RippleEnabled = false;
                BubbleOverlay.BeginChaos(new Border(), _ => paid++, ChaosRunHost.OnDefused);
                ChaosRunHost.OnDefused(snap);
                Assert.Null(ChaosFieldFxOverlay.Active); // boon gate
                Assert.Null(ChaosSkiaFxOverlay.Active);
            }
            finally
            {
                BubbleOverlay.EndChaos();
                BubbleOverlay.Field.Bubbles.Clear();
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = now;
                ChaosSkiaFxOverlay.CloseActive();
                ChaosSkiaFxOverlay.SkipPlatformChecksForTests = skiaChecks;
                ChaosSkiaFxOverlay.Composited = composited;
                settings.ChaosSkiaFxEnabled = enabled;
                stateField.SetValue(null, savedState);
            }
            return Task.CompletedTask;
        });
    }
}
