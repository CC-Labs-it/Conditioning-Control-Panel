using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosSizeQueenTests
{
    // WPF ChaosModeService.cs:2035-2036; BubbleService.cs:1704-1719.
    [Fact]
    public async Task Snap_draws_a_ring_and_sweeps_only_treats_at_the_expanding_front()
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
            int paid = 0;
            try
            {
                var state = new ChaosRunState(new ChaosRunConfig()) { RippleEnabled = true };
                stateField.SetValue(null, state);
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
                var fx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                Assert.True(fx.IsVisible && fx.Clock.IsEnabled);
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
                BubbleOverlay.TriggerChaosRipple(snap);
                Assert.Null(ChaosFieldFxOverlay.Active); // no producer outside a run
                state.RippleEnabled = false;
                BubbleOverlay.BeginChaos(new Border(), _ => paid++, ChaosRunHost.OnDefused);
                ChaosRunHost.OnDefused(snap);
                Assert.Null(ChaosFieldFxOverlay.Active); // boon gate
            }
            finally
            {
                BubbleOverlay.EndChaos();
                BubbleOverlay.Field.Bubbles.Clear();
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = now;
                stateField.SetValue(null, savedState);
            }
            return Task.CompletedTask;
        });
    }
}
