using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoundTetherTests
{
    [Fact]
    public async Task Pair_draws_tracks_resolves_and_clears_on_panic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = BubbleOverlay.Field.Clock;
            var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            try
            {
                BubbleOverlay.Field.Clock = () => now;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                var enraged = new List<AmbientBubble>();
                BubbleOverlay.BeginChaos(new Border(), _ => { }, _ => { }, _ => { }, onBoundEnraged: enraged.Add);
                var (sa, sb) = ChaosBubbleVariants.BuildBoundPair(1);
                var (a, b) = BubbleOverlay.SpawnBoundPair(sa, sb);
                Assert.Equal((-a.Velocity.X, -a.Velocity.Y), b.Velocity);   // WPF MirrorVelocityFrom
                var prims = new List<FieldFxPrim>();
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether));
                a.X += 30;
                BubbleOverlay.TickBoundPairs();
                var old = Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether));
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.NotEqual(old.X, Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether)).X);
                Assert.True(a.Snap());
                BubbleOverlay.Field.ChaosDefused!(a);
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.DoesNotContain(prims, p => p.Kind == FieldFxKind.Tether);
                double fuse = b.FuseRemainingMs;
                now = now.AddMilliseconds(2501);
                BubbleOverlay.TickBoundPairs();
                Assert.Equal(fuse / 2, b.FuseRemainingMs);
                Assert.Same(b, Assert.Single(enraged));
                Assert.True(b.Enraged);
                var (sc, sd) = ChaosBubbleVariants.BuildBoundPair(1);
                var (c, d) = BubbleOverlay.SpawnBoundPair(sc, sd);
                var v = d.Velocity;
                BubbleOverlay.Field.ChaosDetonated!(c);
                Assert.Same(d, enraged[1]);
                Assert.Equal(v.X * ChaosTuning.BOUND_ENRAGE_SPEED_MULT, d.Velocity.X, 8);
                Assert.Equal(v.Y * ChaosTuning.BOUND_ENRAGE_SPEED_MULT, d.Velocity.Y, 8);
                var (se, sf) = ChaosBubbleVariants.BuildBoundPair(1);
                BubbleOverlay.SpawnBoundPair(se, sf);
                BubbleOverlay.EndChaos();
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.DoesNotContain(prims, p => p.Kind == FieldFxKind.Tether);
            }
            finally
            {
                BubbleOverlay.EndChaos();
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                BubbleOverlay.Field.Clock = clock;
            }
            return Task.CompletedTask;
        });
    }

    private sealed class AlwaysHit : Random { public override double NextDouble() => 0; }

    // WPF ChaosModeService.cs:1286-1295 (debut) and :1394-1400 with BubbleService.cs:2760 (enrage feedback).
    [Fact]
    public Task First_pair_debuts_with_longer_fuse_and_enrage_gives_feedback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        bool announcer = settings.ChaosAnnouncerEnabled;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var rng = ChaosRunHost.Rng;
        var popText = ChaosRunHost.PopText;
        var pulseFx = ChaosRunHost.PulseFx;
        var clock = BubbleOverlay.Field.Clock;
        int runs = ChaosMeta.State.RunsCompleted;
        bool seen = ChaosMeta.State.SeenBound;
        var discovered = new HashSet<string>(ChaosMeta.State.DiscoveredCodexIds);
        var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var labels = new List<string>();
        var pulses = new List<Color>();
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, Difficulty = ChaosDifficulty.Hard, HitboxScale = 1.25 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.State!.MagnetEnabled = true;   // silk_touch: hitbox 1.25 plus magnet (WPF BubbleService.cs:1620-1621)
            ChaosMeta.State.RunsCompleted = 0;   // below the Chaperone and Tease ranks; Hard alone admits the Bound
            ChaosMeta.State.SeenBound = false;
            ChaosRunHost.Rng = new AlwaysHit();
            settings.ChaosAnnouncerEnabled = true;
            LocalizationManager.Instance.Initialize("en");
            ChaosAnnouncerOverlay.CloseActive();
            BubbleOverlay.Field.Clock = () => now;
            ChaosRunHost.PopText = (_, _, text, _) => labels.Add(text);
            ChaosRunHost.PulseFx = (color, _) => pulses.Add(color);
            ChaosRunHost.SpawnTick();
            Assert.True(ChaosMeta.State.SeenBound);
            Assert.Equal("⛓ THE BOUND: both, and quickly", ChaosAnnouncerOverlay.Active!.Current!.Text);
            Assert.Equal($"{IconMap.Bound} two of them, one thread", ChaosRunHost.State!.RecentEvents[0]);
            var halves = BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsBoundHalf == true).ToList();
            Assert.Equal(2, halves.Count);
            Assert.All(halves, h => Assert.Equal(Math.Round(h.Size * 1.75), h.HitSize));   // live half: 1.25 x 1.4 magnet
            var state = ChaosRunHost.State!;
            double eff = ChaosFieldMath.EffectiveIntensity(state.RunIntensity, state.Config.DifficultyMult);
            // The shortest Bound variant fuse is 3500 ms; only the 1.5x debut lifts both halves past 5250.
            double floor = 3500 * (1.0 - eff * 0.25) * state.FuseTimeMult * ChaosTuning.DEBUT_FUSE_MULT;
            Assert.All(halves, h => Assert.True(h.Spec!.FuseMs >= (int)floor, $"fuse {h.Spec!.FuseMs} < {floor}"));
            Assert.True(halves[0].Snap());
            BubbleOverlay.Field.ChaosDefused!(halves[0]);
            labels.Clear(); pulses.Clear();
            now = now.AddMilliseconds(ChaosTuning.BOUND_WINDOW_MS + 1);
            BubbleOverlay.TickBoundPairs();
            Assert.True(halves[1].Enraged);
            Assert.Contains("ENRAGED", labels);
            Assert.Contains(Color.FromRgb(0xFF, 0x4A, 0x4A), pulses);
            Assert.Equal($"{IconMap.Bound} the tether snaps. it enrages", ChaosRunHost.State!.RecentEvents[0]);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosAnnouncerOverlay.CloseActive();
            ChaosRunHost.Rng = rng;
            ChaosRunHost.PopText = popText;
            ChaosRunHost.PulseFx = pulseFx;
            BubbleOverlay.Field.Clock = clock;
            ChaosMeta.State.RunsCompleted = runs;
            ChaosMeta.State.SeenBound = seen;
            ChaosMeta.State.DiscoveredCodexIds.Clear();
            ChaosMeta.State.DiscoveredCodexIds.UnionWith(discovered);
            settings.ChaosAnnouncerEnabled = announcer;
            LocalizationManager.Instance.SetLanguage(language);
        }
        return Task.CompletedTask;
    });
}
