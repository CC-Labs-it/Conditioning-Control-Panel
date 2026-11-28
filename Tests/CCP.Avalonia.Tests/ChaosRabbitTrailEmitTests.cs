using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosRabbitTrailEmitTests
{
    [Fact]
    public void DarterTrailsRouteAndRespectSweeperAndPopping() => AvaloniaTestDispatcher.Run(() =>
    {
        var stateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldState = stateField.GetValue(null);
        var settings = CoreSettings.Current;
        bool enabled = settings.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var field = BubbleOverlay.Field;
        var previousBubbles = field.Bubbles.ToArray();
        bool held = field.HoldChaos, frozen = field.FreezeChaos;
        double vibrate = field.FreezeVibrateRemainingMs;
        var pulseField = typeof(AmbientBubbleField).GetField("_freezePulsePhase", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pulse = pulseField.GetValue(field);
        bool skiaSkip = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
        bool fallbackSkip = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        // Core darter motion (#5722) retires a darter once LifetimeMs runs out (at least 1 ms), so every
        // darter here lives long enough for the steps the test takes, as real darter specs always do.
        const int Life = 60_000;
        static AmbientBubble Bubble(EffectBubbleSpec spec, double x)
        {
            var b = AmbientBubble.SpawnChaos(new Random(7), 0, 0, 0, 1000, 1000, 1, spec, ChaosMotion.RoamBounce);
            b.X = x; b.Y = 150; b.Size = 0;
            return b;
        }
        try
        {
            field.Bubbles.Clear();
            field.HoldChaos = false;
            field.FreezeChaos = false;
            var state = new ChaosRunState(new ChaosRunConfig()) { RabbitTrailSec = 2 };
            stateField.SetValue(null, state);
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            // A newly enabled trail must wait until the frozen darter resumes.
            state.RabbitTrailSec = 0;
            field.FreezeChaos = true;
            var frozenRabbit = Bubble(new EffectBubbleSpec { LifetimeMs = Life, IsDarter = true }, 30);
            field.Bubbles.Add(frozenRabbit);
            BubbleOverlay.StepChaosField();
            state.RabbitTrailSec = 2;
            BubbleOverlay.StepChaosField();
            Assert.Null(ChaosSkiaFxOverlay.Active);
            field.FreezeChaos = false;
            BubbleOverlay.StepChaosField();
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            ChaosSkiaFxOverlay.CloseActive();
            field.Bubbles.Clear();
            var rabbit = Bubble(new EffectBubbleSpec { LifetimeMs = Life, IsDarter = true }, 0);
            field.Bubbles.Add(rabbit);
            rabbit.X = 30;
            BubbleOverlay.StepChaosField();
            var fx = ChaosSkiaFxOverlay.Active!.Fx;
            var particles = (Array)fx.GetType().GetField("_p", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx)!;
            var first = particles.GetValue(0)!;
            Assert.True((float)first.GetType().GetField("VX")!.GetValue(first)! < 0);
            Assert.Equal((byte)0, (byte)first.GetType().GetField("Kind")!.GetValue(first)!);
            var countField = fx.GetType().GetField("_n", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int count = (int)countField.GetValue(fx)!;
            rabbit.X += 30;
            BubbleOverlay.StepChaosField();
            Assert.True((int)countField.GetValue(fx)! > count);
            ChaosSkiaFxOverlay.CloseActive();
            field.Bubbles.Clear();
            state.RabbitTrailSec = 0;
            var sweeper = Bubble(new EffectBubbleSpec { LifetimeMs = Life, IsDarter = true, IsSweeper = true }, 30);
            field.Bubbles.Add(sweeper);
            BubbleOverlay.StepChaosField();
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            fx = ChaosSkiaFxOverlay.Active!.Fx;
            particles = (Array)fx.GetType().GetField("_p", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx)!;
            first = particles.GetValue(0)!;
            Assert.Equal((byte)1, (byte)first.GetType().GetField("Kind")!.GetValue(first)!);
            ChaosSkiaFxOverlay.CloseActive();
            field.Bubbles.Clear();
            settings.ChaosSkiaFxEnabled = false;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            field.Bubbles.Add(Bubble(new EffectBubbleSpec { LifetimeMs = Life, IsDarter = true, IsSweeper = true }, 60));
            BubbleOverlay.StepChaosField();
            var prims = new List<FieldFxPrim>();
            ChaosFieldFxOverlay.Active!.Model.Frame(ChaosFieldFxOverlay.NowMs(), prims);
            Assert.Contains(prims, p => p.Kind == FieldFxKind.TrailDot && p.Warm);
            ChaosFieldFxOverlay.CloseActive();
            field.Bubbles.Clear();
            settings.ChaosSkiaFxEnabled = true;
            field.Bubbles.Add(new AmbientBubble { X = 60, Size = 0, Popping = true, Spec = new EffectBubbleSpec { LifetimeMs = Life, IsDarter = true, IsSweeper = true } });
            field.Bubbles.Add(Bubble(new EffectBubbleSpec { IsSweeper = true }, 60));
            BubbleOverlay.StepChaosField();
            Assert.Null(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            field.Bubbles.Clear();
            field.Bubbles.AddRange(previousBubbles);
            field.HoldChaos = held; field.FreezeChaos = frozen;
            field.FreezeVibrateRemainingMs = vibrate;
            pulseField.SetValue(field, pulse);
            stateField.SetValue(null, oldState);
            settings.ChaosSkiaFxEnabled = enabled;
            ChaosSkiaFxOverlay.CloseActive(); ChaosFieldFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = skiaSkip;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = fallbackSkip;
        }
    });

    [Fact]
    public void TrailTestRestoresSharedField() => AvaloniaTestDispatcher.Run(() =>
    {
        var field = BubbleOverlay.Field;
        var original = field.Bubbles.ToArray();
        bool held = field.HoldChaos, frozen = field.FreezeChaos;
        try
        {
            var first = new AmbientBubble();
            var second = new AmbientBubble();
            field.Bubbles.Clear();
            field.Bubbles.Add(first); field.Bubbles.Add(second);
            field.HoldChaos = field.FreezeChaos = true;
            DarterTrailsRouteAndRespectSweeperAndPopping();
            Assert.Equal(new[] { first, second }, field.Bubbles);
            Assert.True(field.HoldChaos);
            Assert.True(field.FreezeChaos);
        }
        finally
        {
            field.Bubbles.Clear(); field.Bubbles.AddRange(original);
            field.HoldChaos = held; field.FreezeChaos = frozen;
        }
    });

}
