using System;
using System.Collections.Generic;
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
        try
        {
            field.Bubbles.Clear();
            field.HoldChaos = false;
            field.FreezeChaos = true;
            var state = new ChaosRunState(new ChaosRunConfig()) { RabbitTrailSec = 2 };
            stateField.SetValue(null, state);
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            var rabbit = new AmbientBubble { X = 0, Y = 0, Size = 0, Spec = new EffectBubbleSpec { IsDarter = true } };
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
            var sweeper = new AmbientBubble { X = 30, Size = 0, Spec = new EffectBubbleSpec { IsDarter = true, IsSweeper = true } };
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
            field.Bubbles.Add(new AmbientBubble { X = 60, Size = 0, Spec = new EffectBubbleSpec { IsDarter = true, IsSweeper = true } });
            BubbleOverlay.StepChaosField();
            var prims = new List<FieldFxPrim>();
            ChaosFieldFxOverlay.Active!.Model.Frame(ChaosFieldFxOverlay.NowMs(), prims);
            Assert.Contains(prims, p => p.Kind == FieldFxKind.TrailDot && p.Warm);
            ChaosFieldFxOverlay.CloseActive();
            field.Bubbles.Clear();
            settings.ChaosSkiaFxEnabled = true;
            field.Bubbles.Add(new AmbientBubble { X = 60, Size = 0, Popping = true, Spec = new EffectBubbleSpec { IsDarter = true, IsSweeper = true } });
            field.Bubbles.Add(new AmbientBubble { X = 60, Size = 0, Spec = new EffectBubbleSpec { IsSweeper = true } });
            BubbleOverlay.StepChaosField();
            Assert.Null(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            field.Bubbles.Clear(); field.FreezeChaos = false;
            stateField.SetValue(null, oldState);
            settings.ChaosSkiaFxEnabled = enabled;
            ChaosSkiaFxOverlay.CloseActive(); ChaosFieldFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
        }
    });
}
