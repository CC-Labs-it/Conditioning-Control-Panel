using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubbleService.cs:3929-3951, 1763-1780 (Tail-Plug trail).
/// Borrows the shared BubbleOverlay.Field, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public class ChaosRabbitTrailTests
{
    private static readonly FieldInfo StateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo StepTrails = typeof(BubbleOverlay).GetMethod("StepRabbitTrails", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task WithField(Action<ChaosRunState> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = StateField.GetValue(null);
        var incoming = BubbleOverlay.Field.Bubbles.ToArray();
        var fieldChecks = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        var firer = EffectPayload.Firer;
        var skia = ConditioningControlPanel.CoreSettings.Current.ChaosSkiaFxEnabled;
        try
        {
            ConditioningControlPanel.CoreSettings.Current.ChaosSkiaFxEnabled = false;
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            EffectPayload.Firer = null;
            var state = new ChaosRunState(new ChaosRunConfig());
            StateField.SetValue(null, state);
            BubbleOverlay.BeginChaos(new Border(), _ => { });
            body(state);
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.AddRange(incoming);
            ChaosFieldFxOverlay.CloseActive(); ChaosFieldFxOverlay.SkipPlatformChecksForTests = fieldChecks;
            EffectPayload.Firer = firer;
            ConditioningControlPanel.CoreSettings.Current.ChaosSkiaFxEnabled = skia; StateField.SetValue(null, old);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TailPlugRabbitRecordsATrailThatPopsTreatsWithin46Px() => WithField(state =>
    {
        state.RabbitTrailSec = 2.0;
        var rabbit = new AmbientBubble { X = 100, Y = 100, Size = 20, Spec = ChaosBubbleVariants.BuildDarter(0.5) };
        var treat = new AmbientBubble { X = 300, Y = 330, Size = 20, Spec = new EffectBubbleSpec { Payload = new FlashPayload() } };
        BubbleOverlay.Field.Bubbles.AddRange(new[] { rabbit, treat });
        BubbleOverlay.RabbitTrailSteps[rabbit] = 100;   // past the telegraph (no trail while it flares)
        StepTrails.Invoke(null, null);
        Assert.Single(BubbleOverlay.RabbitTrails[rabbit].Points);
        Assert.NotNull(ChaosFieldFxOverlay.Active);   // the point is mirrored as a pink trail spark
        rabbit.X = 110; StepTrails.Invoke(null, null);   // under the 40 DIP gap: no new point
        Assert.Single(BubbleOverlay.RabbitTrails[rabbit].Points);
        rabbit.X = 300; rabbit.Y = 290; StepTrails.Invoke(null, null);   // point at (310,300), treat centre (310,340): 40 px
        Assert.Equal(2, BubbleOverlay.RabbitTrails[rabbit].Points.Count);
        Assert.True(treat.Popping);

        state.RabbitTrailSec = 0;   // boon off: a plain rabbit drops its trail and pops nothing
        StepTrails.Invoke(null, null);
        Assert.Empty(BubbleOverlay.RabbitTrails[rabbit].Points);
    });

    [Fact]
    public Task SweeperDragsATrailWithoutTheBoonButOnlyTailPlugPops() => WithField(state =>
    {
        var sweeper = new AmbientBubble { X = 100, Y = 100, Size = 20, Spec = ChaosBubbleVariants.BuildDarter(0.5, sweeper: true) };
        var treat = new AmbientBubble { X = 100, Y = 120, Size = 20, Spec = new EffectBubbleSpec { Payload = new FlashPayload() } };
        BubbleOverlay.Field.Bubbles.AddRange(new[] { sweeper, treat });
        BubbleOverlay.RabbitTrailSteps[sweeper] = 100;   // past the telegraph (no trail while it flares)
        StepTrails.Invoke(null, null);
        Assert.Single(BubbleOverlay.RabbitTrails[sweeper].Points);
        Assert.False(treat.Popping);
    });
}
