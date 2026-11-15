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
        var (saved, clock, nowMs, skip) = (stateField.GetValue(null), BubbleOverlay.Field.Clock, ChaosFieldFxOverlay.NowMs, ChaosFieldFxOverlay.SkipPlatformChecksForTests); var popText = ChaosRunHost.PopText; var labels = new List<string>();
        var settings = ConditioningControlPanel.CoreSettings.Current; var (skia, composited) = (settings.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try
        {
            var state = new ChaosRunState(new ChaosRunConfig()) { RabbitTrailSec = 2 };
            stateField.SetValue(null, state); BubbleOverlay.Field.Clock = () => t;
            ChaosFieldFxOverlay.SkipPlatformChecksForTests = true; settings.ChaosSkiaFxEnabled = false;
            ChaosFieldFxOverlay.NowMs = () => (t - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            int treats = 0, snaps = 0;
            BubbleOverlay.BeginChaos(new Border(), _ => treats++, _ => snaps++);
            var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true, SizePx = 20, LifetimeMs = 8000, DarterSpeed = 9 }, new Random(7));
            ChaosRunHost.PopText = (_, _, label, _) => labels.Add(label); rabbit.X = rabbit.Y = 500; var origin = BubbleOverlay.CenterPx(rabbit); BubbleOverlay.TriggerPlayerRipple(new Point(origin.X - 40, origin.Y), 100, 128); BubbleOverlay.StepPlayerRipples(); Assert.False(rabbit.Spanked); BubbleOverlay.StepPlayerRipples(); Assert.True(rabbit.Spanked); Assert.False(rabbit.Popping); Assert.Equal(1, rabbit.SpankGrowth); Assert.Single(labels, "FLUNG");
            var victim = new AmbientBubble { X = rabbit.X, Y = rabbit.Y, Size = 10, Spec = new EffectBubbleSpec() }; BubbleOverlay.Field.Bubbles.Add(victim); BubbleOverlay.TickDarterSweeps(); Assert.True(victim.Popping); Assert.Equal(1, treats); BubbleOverlay.Field.Bubbles.Remove(victim); treats = 0;
            BubbleOverlay.TriggerPlayerRipple(new Point(origin.X + 40, origin.Y), 100, 128); BubbleOverlay.StepPlayerRipples(); BubbleOverlay.StepPlayerRipples(); double oldX = rabbit.X; rabbit.Step(); Assert.Equal(oldX - 9 * ChaosTuning.RIPPLE_FLING_SPEED_MULT, rabbit.X, 6); Assert.Single(labels, "FLUNG");
            ChaosFieldFxOverlay.CloseActive(); rabbit.X = 30; rabbit.Y = -10; BubbleOverlay.TickChaosTrails();
            var dots = new List<FieldFxPrim>(); var fx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
            fx.Model.Frame(0, dots); Assert.Single(dots, p => p.Kind == FieldFxKind.TrailDot && !p.Warm);
            rabbit.X = 69; BubbleOverlay.TickChaosTrails(); fx.Model.Frame(0, dots); Assert.Single(dots);
            rabbit.X = 70; BubbleOverlay.TickChaosTrails(); fx.Model.Frame(0, dots); Assert.Equal(2, dots.Count);
            AmbientBubble Add(double x, EffectBubbleSpec spec) {
                var b = new AmbientBubble { X = x - 10, Y = -10, Size = 20, Spec = spec, FuseRemainingMs = 5000, FuseTotalMs = 5000 };
                BubbleOverlay.Field.Bubbles.Add(b); return b;
            }
            var treat = Add(126, new EffectBubbleSpec()); var live = Add(125, new EffectBubbleSpec { IsLive = true }); var far = Add(127, new EffectBubbleSpec());
            var otherRabbit = Add(80, new EffectBubbleSpec { IsDarter = true }); var freeze = Add(80, new EffectBubbleSpec { IsFreeze = true });
            BubbleOverlay.Field.FreezeChaos = true; BubbleOverlay.TickChaosTrails(); Assert.False(treat.Popping);
            BubbleOverlay.Field.FreezeChaos = false; BubbleOverlay.TickChaosTrails();
            Assert.True(treat.Popping); Assert.True(live.Popping); Assert.False(far.Popping);
            Assert.False(otherRabbit.Popping); Assert.False(freeze.Popping);
            Assert.Equal(1, treats); Assert.Equal(1, snaps);
            var flags = new[] { "_running", "_chaosOpenedWindows", "_oneShotWindows" }.Select(n => typeof(BubbleOverlay).GetField(n, BindingFlags.NonPublic | BindingFlags.Static)!).ToArray();
            var flagValues = flags.Select(f => f.GetValue(null)).ToArray();
            var xp = (settings.AmbientBubbleXpDayKey, settings.AmbientBubbleXpPaidToday); var oldFreeze = ConditioningControlPanel.CoreSubliminal.BambiFreezeProvider; int payloads = 0;
            try {
                flags[0].SetValue(null, true); flags[1].SetValue(null, false); flags[2].SetValue(null, false);
                settings.AmbientBubbleXpPaidToday = 0; ConditioningControlPanel.CoreSubliminal.BambiFreezeProvider = () => payloads++;
                var ambient = Add(80, new EffectBubbleSpec { Payload = new BambiFreezePayload() }); ambient.AmbientTrigger = true;
                BubbleOverlay.TickChaosTrails(); Assert.True(ambient.Popping); Assert.Equal(1, payloads); Assert.True(settings.AmbientBubbleXpPaidToday > 0);
            } finally {
                ConditioningControlPanel.CoreSubliminal.BambiFreezeProvider = oldFreeze; (settings.AmbientBubbleXpDayKey, settings.AmbientBubbleXpPaidToday) = xp;
                for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, flagValues[i]);
            }
            t = t.AddSeconds(2.01); BubbleOverlay.TickChaosTrails(); Assert.Empty(BubbleOverlay.ChaosTrails[rabbit].Points);
            state.RabbitTrailSec = 0;
            var sweeper = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true, IsSweeper = true, SizePx = 20, LifetimeMs = 8000, DarterSpeed = 9 }, new Random(7));
            sweeper.X = 190; sweeper.Y = -10;
            var boonOffTreat = Add(200, new EffectBubbleSpec());
            BubbleOverlay.TickChaosTrails(); Assert.False(boonOffTreat.Popping);
            fx.Model.Frame(2010, dots); Assert.Contains(dots, p => p.Kind == FieldFxKind.TrailDot && p.Warm);
            t = t.AddMilliseconds(501); BubbleOverlay.TickChaosTrails(); Assert.Empty(BubbleOverlay.ChaosTrails[sweeper].Points);
            settings.ChaosSkiaFxEnabled = true; ChaosSkiaFxOverlay.Composited = () => false; // WPF :3943-3949: Skia trail, 22 DIP gap
            sweeper.X += 22; BubbleOverlay.TickChaosTrails(); fx.Model.Frame(2511, dots);
            Assert.Single(BubbleOverlay.ChaosTrails[sweeper].Points); Assert.Empty(dots);
            BubbleOverlay.EndChaos(); Assert.Empty(BubbleOverlay.ChaosTrails);
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Field.FreezeChaos = false; BubbleOverlay.Field.Bubbles.Clear(); ChaosFieldFxOverlay.CloseActive();
            (BubbleOverlay.Field.Clock, ChaosFieldFxOverlay.NowMs, ChaosFieldFxOverlay.SkipPlatformChecksForTests) = (clock, nowMs, skip);
            (settings.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited) = (skia, composited); stateField.SetValue(null, saved); ChaosRunHost.PopText = popText;
        }
    });
    [Fact]
    public void Spawned_darter_moves_and_feeds_the_recorder() => AvaloniaTestDispatcher.Run(() =>
    {
        var cues = new List<string>(); int burstAtCatch = -1;
        var (cue, popText, cursor) = (ChaosRunHost.RippleCue, ChaosRunHost.PopText, BubbleOverlay.ReadCursorPx); var settings = ConditioningControlPanel.CoreSettings.Current; var (fxEnabled, fxComposited, fxSkip) = (settings.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited, ChaosSkiaFxOverlay.SkipPlatformChecksForTests);
        try
        {
            ChaosRunHost.RippleCue = (key, _) => cues.Add(key); ChaosRunHost.PopText = (_, _, word, _) => { cues.Add(word); if (word == ConditioningControlPanel.Localization.Loc.Get("chaos_rabbit_banner")) burstAtCatch = ChaosSkiaFxOverlay.Active?.Fx.ParticleCount ?? 0; };
            var cfg = new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false, DartersEnabled = false };
            ChaosRunHost.StartRun(cfg); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!; state.GgRabbitChance = 1;
            var treat = new AmbientBubble { X = 400, Y = 400, Size = 20, Spec = new EffectBubbleSpec() };
            var firer = EffectPayload.Firer;
            try { EffectPayload.Firer = null; ChaosRunHost.OnBubblePopped(treat); }
            finally { EffectPayload.Firer = firer; }
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsSweeper == true));
            Assert.Contains("rabbit_spawn", cues);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaos_gg_rabbits"), state.RecentEvents);
            state.GgRabbitChance = 0;
            var sweeper = BubbleOverlay.Field.Bubbles.First(b => b.Spec?.IsSweeper == true);
            sweeper.X = 1000; sweeper.Y = 500;
            var target = new AmbientBubble { X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec() };
            var live = new AmbientBubble { X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec { IsLive = true }, FuseTotalMs = 5000, FuseRemainingMs = 5000 };
            var frozen = new AmbientBubble { X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec { IsFreeze = true } };
            var otherRabbit = new AmbientBubble { X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec { IsDarter = true } };
            var otherScreen = new AmbientBubble { Screen = 1, X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec() };
            var ambient = new AmbientBubble { AmbientTrigger = true, X = 1001, Y = 501, Size = 20, Spec = new EffectBubbleSpec { Payload = null! } };
            BubbleOverlay.Field.Bubbles.AddRange(new[] { target, live, frozen, otherRabbit, otherScreen, ambient });
            BubbleOverlay.HoldChaos(true); BubbleOverlay.TickDarterSweeps();
            Assert.False(target.Popping || live.Popping); BubbleOverlay.HoldChaos(false);
            try { EffectPayload.Firer = null; BubbleOverlay.TickDarterSweeps(); }
            finally { EffectPayload.Firer = firer; }
            Assert.True(target.Popping && live.Popping && otherScreen.Popping && ambient.Popping);
            Assert.False(frozen.Popping || otherRabbit.Popping);
            ChaosSkiaFxOverlay.CloseActive(); settings.ChaosSkiaFxEnabled = true; ChaosSkiaFxOverlay.Composited = () => true; ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true; BubbleOverlay.Press(sweeper, sweeper.CenterX, sweeper.CenterY);
            try { EffectPayload.Firer = null; BubbleOverlay.PopChaos(sweeper); } // indirect pops also smack
            finally { EffectPayload.Firer = firer; }
            Assert.False(sweeper.Popping); Assert.True(sweeper.Spanked); Assert.Null(ChaosSkiaFxOverlay.Active);
            sweeper.Step(); Assert.True(sweeper.DrawScale > 1.25); // click delivered the smack
            state.RabbitRateMult = 1000; state.RabbitTrailSec = 2; BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick(); Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsDarter == true);
            cfg.DartersEnabled = true; ChaosRunHost.SpawnTick();
            var rabbit = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true));
            Assert.Equal(ChaosMotion.RoamBounce, rabbit.TriggerMotion); rabbit.X = 500; rabbit.Y = 500;
            for (int i = 0; i < 25; i++) BubbleOverlay.StepChaosField();
            Assert.True(BubbleOverlay.ChaosTrails[rabbit].Points.Count >= 2);
            state.Focus = 0; state.SlowMoBonusSec = 2; ChaosSkiaFxOverlay.CloseActive();
            double score = state.Score, multiplier = state.TotalMult, heat = state.Heat; int combo = state.Combo;
            BubbleOverlay.Press(rabbit, rabbit.CenterX, rabbit.CenterY); Assert.True(rabbit.Popping); Assert.InRange(burstAtCatch, 20, 24);
            Assert.Equal(score + 210 * multiplier, state.Score, 6); var center = BubbleOverlay.CenterPx(rabbit); using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(100, 100)); surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            Assert.Equal(15, state.Focus); Assert.Equal(combo + 1, state.Combo); Assert.Equal(Math.Min(1, heat + 0.05), state.Heat, 6); ChaosSkiaFxOverlay.RenderMonitor(surface.Canvas, ChaosSkiaFxOverlay.Active!.Fx, new PixelRect((int)center.X - 50, (int)center.Y - 50, 100, 100), new Size(100, 100));
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaos_rabbit_quick"), state.RecentEvents); using var snapshot = surface.Snapshot(); using var burst = SkiaSharp.SKBitmap.FromImage(snapshot); Assert.True(burst.GetPixel(50, 50).Alpha > 0); Assert.True(burst.GetPixel(70, 50).Red > burst.GetPixel(70, 50).Green);
            Assert.Equal(6 + state.SlowMoBonusSec, ChaosRunHost.SlowMoRemainingSec, 6);
            Assert.Equal(ChaosRunHost.SlowMoFactor, BubbleOverlay.Field.ChaosTimeScale); Assert.Contains("rabbit_catch", cues);
            ChaosSkiaFxOverlay.CloseActive(); settings.ChaosSkiaFxEnabled = false; var another = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildDarter(0), new Random(9));
            BubbleOverlay.HoldChaos(true); BubbleOverlay.Press(another, another.CenterX, another.CenterY);
            Assert.False(another.Popping); BubbleOverlay.HoldChaos(false);
            score = state.Score; multiplier = state.TotalMult; BubbleOverlay.Field.FreezeChaos = true;
            BubbleOverlay.Press(another, another.CenterX, another.CenterY); BubbleOverlay.Field.FreezeChaos = false;
            Assert.Equal(score + 120 * multiplier, state.Score, 6); Assert.Equal(0, burstAtCatch); Assert.Null(ChaosSkiaFxOverlay.Active); // telegraph catches are not quick; no Skia burst when disabled
            Assert.Equal(6.8 + state.SlowMoBonusSec, ChaosRunHost.SlowMoRemainingSec, 6);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("chaos_rabbit_extend"), state.RecentEvents);
            var indirect = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildDarter(0), new Random(4)); BubbleOverlay.PopChaos(indirect);
            Assert.True(indirect.Popping); Assert.Equal(7.6 + state.SlowMoBonusSec, ChaosRunHost.SlowMoRemainingSec, 6);
            state.SpankerActive = true; state.SpankGrowFactor = 1.5;
            var ally = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildDarter(0), new Random(5)); BubbleOverlay.Press(ally, ally.CenterX, ally.CenterY);
            Assert.False(ally.Popping); Assert.True(ally.Spanked); Assert.Equal(1.5, ally.SpankGrowth); Assert.Single(cues, "SPANKED");
            BubbleOverlay.Field.Bubbles.Clear(); var homer = BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildDarter(0), new Random(5));
            homer.X = homer.Y = 500; while (homer.DarterTelegraphing) homer.Step();
            double x0 = homer.X, y0 = homer.Y; homer.Step(); double h = Math.Atan2(homer.Y - y0, homer.X - x0); (x0, y0) = (homer.X, homer.Y);
            BubbleOverlay.ReadCursorPx = () => new PixelPoint((int)(homer.CenterX - 300 * Math.Sin(h)), (int)(homer.CenterY + 300 * Math.Cos(h)));
            state.CursorPullStrength = 1; BubbleOverlay.StepChaosField(); // WPF :3877-3893: The Pull turns it toward the cursor
            Assert.True(Math.IEEERemainder(Math.Atan2(homer.Y - y0, homer.X - x0) - h, 2 * Math.PI) > 0.01);
        }
        finally { ChaosRunHost.ForceShutdown(); (ChaosRunHost.RippleCue, ChaosRunHost.PopText, BubbleOverlay.ReadCursorPx) = (cue, popText, cursor); (settings.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited, ChaosSkiaFxOverlay.SkipPlatformChecksForTests) = (fxEnabled, fxComposited, fxSkip); }
    });

    [Fact]
    public void Telegraph_ring_is_rendered_then_disappears() => AvaloniaTestDispatcher.Run(() =>
    {
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 240, 240), 1) { Width = 240, Height = 240 }; var oldImage = BubbleOverlay.Image; BubbleOverlay.Image = null;
        try
        {
            var spec = new EffectBubbleSpec { IsDarter = true, SizePx = 100, TelegraphMs = 64, LifetimeMs = 8000, DarterSpeed = 9, Tint = 0xFFFF4DC4 };
            var b = BubbleOverlay.SpawnChaos(spec, new Random(7));
            b.X = b.Y = 70;
            w.Show(); w.UpdateLayout();
            byte[] Frame()
            {
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(240, 240));
                bitmap.Render((Control)w.Content!); using var png = new System.IO.MemoryStream(); bitmap.Save(png); return png.ToArray();
            }
            var flare = Frame(); using var ordinary = SkiaSharp.SKBitmap.Decode(flare); var glow = ordinary.GetPixel(70, 120); Assert.True(glow.Alpha > 0 && glow.Red > glow.Green);
            b.Step(); b.Step(); b.Scale = 1; var plain = Frame();
            Assert.False(flare.SequenceEqual(plain)); b.Spec = new EffectBubbleSpec { IsDarter = true, IsSweeper = true, Tint = spec.Tint }; using var sweeper = SkiaSharp.SKBitmap.Decode(Frame()); Assert.True(sweeper.GetPixel(70, 120).Green > sweeper.GetPixel(70, 120).Blue); b.Spec = spec;
            b.Spank(new Random(1), 1); b.Scale = 1; var allyFrame = Frame(); Assert.False(plain.SequenceEqual(allyFrame)); using var ally = SkiaSharp.SKBitmap.Decode(allyFrame); Assert.True(ally.GetPixel(70, 120).Blue > ally.GetPixel(70, 120).Green); // WPF :4759 ally glow
        }
        finally { BubbleOverlay.Image = oldImage; BubbleOverlay.Field.Bubbles.Clear(); w.Close(); }
    });

    [Fact]
    public void Rabbit_feedback_is_present_in_all_ten_languages()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        if (!System.IO.Directory.Exists(dir)) dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Languages");
        var files = System.IO.Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(f));
            foreach (var key in new[] { "chaos_gg_rabbits", "chaos_rabbit_caught", "chaos_rabbit_quick", "chaos_rabbit_extend", "chaos_rabbit_banner" })
                Assert.False(string.IsNullOrWhiteSpace((string?)json[key]), $"{f}: {key}");
        }
    }

}
