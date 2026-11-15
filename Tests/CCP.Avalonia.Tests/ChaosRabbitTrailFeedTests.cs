using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosRabbitTrailFeedTests
{
    [Fact]
    public void Trails_draw_pop_expire_and_clear() => AvaloniaTestDispatcher.Run(() =>
    {
        var stateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.Static | BindingFlags.NonPublic)!;
        var saved = stateField.GetValue(null);
        var clock = BubbleOverlay.Field.Clock;
        var nowMs = ChaosFieldFxOverlay.NowMs;
        var skip = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try
        {
            var state = new ChaosRunState(new ChaosRunConfig()) { RabbitTrailSec = 2 };
            stateField.SetValue(null, state);
            BubbleOverlay.Field.Clock = () => t;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
            ChaosFieldFxOverlay.NowMs = () => (t - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            int treats = 0, snaps = 0;
            BubbleOverlay.BeginChaos(new Border(), _ => treats++, _ => snaps++);
            var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true, SizePx = 20, LifetimeMs = 8000, DarterSpeed = 9 }, new Random(7));
            rabbit.X = 30; rabbit.Y = -10;
            BubbleOverlay.TickChaosTrails();
            var dots = new List<FieldFxPrim>();
            var fx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
            fx.Model.Frame(0, dots);
            Assert.Single(dots.Where(p => p.Kind == FieldFxKind.TrailDot && !p.Warm));
            rabbit.X = 69;
            BubbleOverlay.TickChaosTrails();
            fx.Model.Frame(0, dots);
            Assert.Single(dots);
            rabbit.X = 70;
            BubbleOverlay.TickChaosTrails();
            fx.Model.Frame(0, dots);
            Assert.Equal(2, dots.Count);
            AmbientBubble Add(double x, EffectBubbleSpec spec) {
                var b = new AmbientBubble { X = x - 10, Y = -10, Size = 20, Spec = spec, FuseRemainingMs = 5000, FuseTotalMs = 5000 };
                BubbleOverlay.Field.Bubbles.Add(b); return b;
            }
            var treat = Add(126, new EffectBubbleSpec());
            var live = Add(125, new EffectBubbleSpec { IsLive = true });
            var far = Add(127, new EffectBubbleSpec());
            var otherRabbit = Add(80, new EffectBubbleSpec { IsDarter = true });
            var freeze = Add(80, new EffectBubbleSpec { IsFreeze = true });
            BubbleOverlay.Field.FreezeChaos = true;
            BubbleOverlay.TickChaosTrails();
            Assert.False(treat.Popping);
            BubbleOverlay.Field.FreezeChaos = false;
            BubbleOverlay.TickChaosTrails();
            Assert.True(treat.Popping); Assert.True(live.Popping); Assert.False(far.Popping);
            Assert.False(otherRabbit.Popping); Assert.False(freeze.Popping);
            Assert.Equal(1, treats); Assert.Equal(1, snaps);
            t = t.AddSeconds(2.01);
            BubbleOverlay.TickChaosTrails();
            Assert.Empty(BubbleOverlay.ChaosTrails[rabbit].Points);
            state.RabbitTrailSec = 0;
            var sweeper = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true, IsSweeper = true, SizePx = 20, LifetimeMs = 8000, DarterSpeed = 9 }, new Random(7));
            sweeper.X = 190; sweeper.Y = -10;
            var boonOffTreat = Add(200, new EffectBubbleSpec());
            BubbleOverlay.TickChaosTrails();
            Assert.False(boonOffTreat.Popping);
            fx.Model.Frame(2010, dots);
            Assert.Contains(dots, p => p.Kind == FieldFxKind.TrailDot && p.Warm);
            t = t.AddMilliseconds(501);
            BubbleOverlay.TickChaosTrails();
            Assert.Empty(BubbleOverlay.ChaosTrails[sweeper].Points);
            BubbleOverlay.EndChaos();
            Assert.Empty(BubbleOverlay.ChaosTrails);
        }
        finally
        {
            BubbleOverlay.EndChaos();
            BubbleOverlay.Field.FreezeChaos = false;
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Clock = clock;
            ChaosFieldFxOverlay.CloseActive();
            ChaosFieldFxOverlay.NowMs = nowMs;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = skip;
            stateField.SetValue(null, saved);
        }
    });
    [Fact]
    public void Spawned_darter_moves_and_feeds_the_recorder() => AvaloniaTestDispatcher.Run(() =>
    {
        try
        {
            var cfg = new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false, DartersEnabled = false };
            ChaosRunHost.StartRun(cfg);
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            state.GgRabbitChance = 1;
            var treat = new AmbientBubble { X = 400, Y = 400, Size = 20, Spec = new EffectBubbleSpec() };
            var firer = EffectPayload.Firer;
            try { EffectPayload.Firer = null; ChaosRunHost.OnBubblePopped(treat); }
            finally { EffectPayload.Firer = firer; }
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsSweeper == true));
            state.GgRabbitChance = 0;
            state.RabbitRateMult = 1000;
            state.RabbitTrailSec = 2;
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick();
            Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsDarter == true);
            cfg.DartersEnabled = true;
            ChaosRunHost.SpawnTick();
            var rabbit = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true));
            Assert.Equal(ChaosMotion.RoamBounce, rabbit.TriggerMotion);
            BubbleOverlay.Press(rabbit, rabbit.CenterX, rabbit.CenterY);
            Assert.False(rabbit.Popping);
            rabbit.X = 500; rabbit.Y = 500;
            for (int i = 0; i < 25; i++) BubbleOverlay.StepChaosField();
            Assert.True(BubbleOverlay.ChaosTrails[rabbit].Points.Count >= 2);
        }
        finally { ChaosRunHost.ForceShutdown(); }
    });

}
