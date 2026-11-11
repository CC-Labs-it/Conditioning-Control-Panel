using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosTeaseBurstTests
{
    private sealed class ZeroRandom : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task Debut_uses_the_selected_language() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var seen = ChaosMeta.State.SeenTease;
        var runs = ChaosMeta.State.RunsCompleted;
        var rng = ChaosRunHost.Rng;
        var announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
        try
        {
            loc.SetLanguage("es");
            ChaosMeta.State.SeenTease = false;
            ChaosMeta.State.RunsCompleted = 10;
            CoreSettings.Current.ChaosAnnouncerEnabled = true;
            ChaosRunHost.Rng = new ZeroRandom();
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosAnnouncerOverlay.CloseActive();
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick();
            var cue = ChaosAnnouncerOverlay.Active!.Current!;
            Assert.Equal("LA TENTACIÓN", cue.Text);
            Assert.Equal("no la toques", cue.SubText);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosAnnouncerOverlay.CloseActive();
            ChaosRunHost.Rng = rng;
            ChaosMeta.State.SeenTease = seen;
            ChaosMeta.State.RunsCompleted = runs;
            CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
            loc.SetLanguage(language);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Tease_spawn_touch_and_off_switch_follow_the_run_path() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled; var performanceMode = CoreSettings.Current.PerformanceMode;
        var announcerEnabled = CoreSettings.Current.ChaosAnnouncerEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var rng = ChaosRunHost.Rng;
        var popText = ChaosRunHost.PopText;
        var runs = ChaosMeta.State.RunsCompleted;
        var firer = EffectPayload.Firer;
        var image = BubbleOverlay.Image;
        var clock = BubbleOverlay.Field.Clock;
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var assets = System.IO.Directory.CreateTempSubdirectory("ccp-tease-face-").FullName;
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(assets, "teasebubble"));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(assets, "teasebubble", "face.gif"), Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7"));
        var assetProvider = CorePaths.EffectiveAssetsProvider;
        CorePaths.EffectiveAssetsProvider = () => assets;
        try
        {
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            CoreSettings.Current.ChaosSkiaFxEnabled = true; CoreSettings.Current.PerformanceMode = true;
            ChaosMeta.State.RunsCompleted = 0;
            ChaosRunHost.Rng = new ZeroRandom();
            var config = new ChaosRunConfig { DurationSec = 60, WaveCount = 3, Difficulty = ChaosDifficulty.Medium };
            ChaosRunHost.StartRun(config);
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick();
            Assert.False(Assert.Single(BubbleOverlay.Field.Bubbles).Spec!.IsTease); // rank gate
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosMeta.State.RunsCompleted = 10;
            config.ScriptedFirstRun = true;
            ChaosRunHost.SpawnTick();
            Assert.False(Assert.Single(BubbleOverlay.Field.Bubbles).Spec!.IsTease);
            BubbleOverlay.Field.Bubbles.Clear();
            config.ScriptedFirstRun = false;
            ChaosRunHost.SpawnTick();
            var bubble = Assert.Single(BubbleOverlay.Field.Bubbles);
            Assert.True(bubble.Spec!.IsTease);
            await BubbleOverlay.LastTeaseLoad!; Dispatcher.UIThread.RunJobs();
            Assert.True(BubbleOverlay.TeaseFaces[bubble].Frames.Count > 1);
            var second = BubbleOverlay.SpawnChaos(bubble.Spec);
            await BubbleOverlay.LastTeaseLoad!; Dispatcher.UIThread.RunJobs();
            var third = BubbleOverlay.SpawnChaos(bubble.Spec);
            await BubbleOverlay.LastTeaseLoad!; Dispatcher.UIThread.RunJobs();
            Assert.True(BubbleOverlay.TeaseFaces[second].Frames.Count > 1);
            Assert.Single(BubbleOverlay.TeaseFaces[third].Frames);
            BubbleOverlay.Field.Bubbles.Remove(second); BubbleOverlay.Field.Bubbles.Remove(third);
            CorePaths.EffectiveAssetsProvider = () => System.IO.Path.Combine(assets, "missing");
            bubble.X = 150; bubble.Y = 150; bubble.Size = 100; bubble.Angle = 0;
            var face = BubbleOverlay.TeaseFaces[bubble];
            BubbleOverlay.TeaseFaces[bubble] = (face.Frames, TimeSpan.FromHours(1), Environment.TickCount64);
            BubbleOverlay.Image = null;
            var window = new BubbleOverlayWindow(0, new PixelRect(0, 0, 400, 400), 1) { Width = 400, Height = 400 };
            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                var px = new byte[4]; var outside = new byte[4];
                int pxX = (int)(200 * frame.PixelSize.Width / window.Bounds.Width);
                int pxY = (int)(200 * frame.PixelSize.Height / window.Bounds.Height);
                using (var fb = frame.Lock())
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address + pxY * fb.RowBytes + pxX * 4, px, 0, 4);
                Assert.True(px[0] > 180 && px[0] > px[1] * 2, $"the Tease must show its red cross: {string.Join(",", px)} at {pxX},{pxY}");
                using (var fb = frame.Lock()) { System.Runtime.InteropServices.Marshal.Copy(fb.Address + (pxY - 20) * fb.RowBytes + (pxX - 20) * 4, px, 0, 4); System.Runtime.InteropServices.Marshal.Copy(fb.Address + (pxY - 31) * fb.RowBytes + (pxX - 31) * 4, outside, 0, 4); }
                Assert.True(px[1] >= 20, $"the face must show its shimmer: {string.Join(",", px)}");
                Assert.True(outside[1] > outside[0] * 0.20, $"the face must be clipped: {string.Join(",", outside)}");
                using (var fb = frame.Lock())
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address + pxY * fb.RowBytes + (pxX + 20) * 4, px, 0, 4);
                Assert.True(Math.Max(px[0], Math.Max(px[1], px[2])) - Math.Min(px[0], Math.Min(px[1], px[2])) > 80, "the media face must be drawn inside the bubble");
                var firstFrame = (px[0], px[1], px[2]);
                BubbleOverlay.TeaseFaces[bubble] = (face.Frames, TimeSpan.FromHours(1), Environment.TickCount64 - 3600000);
                ((global::Avalonia.Controls.Control)window.Content!).InvalidateVisual();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var nextFrame = window.CaptureRenderedFrame()!;
                using (var fb = nextFrame.Lock())
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address + pxY * fb.RowBytes + (pxX + 20) * 4, px, 0, 4);
                Assert.NotEqual(firstFrame, (px[0], px[1], px[2]));
            }
            finally { window.Close(); }
            BubbleOverlay.PopChaos(bubble); // sweep is immune
            Assert.False(bubble.Popping);
            var state = ChaosRunHost.State!;
            state.Combo = 9;
            state.Shields = 1;
            state.Heat = 0.7;
            int labels = 0, fires = 0;
            EffectPayload.Firer = _ => fires++;
            ChaosRunHost.PopText = (x, y, word, color) =>
            {
                if (word != "✖") return;
                labels++;
                Assert.True(bubble.Popping);
                Assert.Equal(200, x);
                Assert.Equal(200, y);
                Assert.Equal(Color.FromRgb(0xFF, 0x3D, 0x5A), color);
                var fx = ChaosSkiaFxOverlay.Active!;
                Assert.InRange(fx.Fx.ParticleCount, 20, 24); // the burst precedes the callback
                var field = fx.Fx.GetType().GetField("_p", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var particle = ((Array)field.GetValue(fx.Fx)!).GetValue(0)!;
                var type = particle.GetType();
                float Read(string name) => (float)type.GetField(name)!.GetValue(particle)!;
                var stage = (PixelRect)typeof(ChaosSkiaFxOverlay).GetField("_stagePx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx)!;
                var scale = (double)typeof(ChaosSkiaFxOverlay).GetField("_scale", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx)!;
                Assert.Equal((200 - stage.X) / scale, Read("X"), 3);
                Assert.Equal((200 - stage.Y) / scale, Read("Y"), 3);
                Assert.Equal(24 * 1.4, Read("Size"), 3);
                Assert.Equal(0xFF3D5Au, (uint)type.GetField("Col")!.GetValue(particle)!);
            };
            BubbleOverlay.HoldChaos(true);
            BubbleOverlay.Press(bubble, 200, 200);
            Assert.False(bubble.Popping);
            BubbleOverlay.HoldChaos(false);
            BubbleOverlay.Press(bubble, 200, 200);
            Assert.Equal(1, labels);
            Assert.Equal(4, state.Combo);
            Assert.Equal(0, state.Shields);
            Assert.Equal(0.5, state.Heat, 3);
            Assert.Equal(1, state.Detonated);
            Assert.Equal(0, fires); // resistance absorbs the payload
            var count = ChaosSkiaFxOverlay.Active!.Fx.ParticleCount;
            BubbleOverlay.Press(bubble, 200, 200);
            Assert.Equal(1, labels);
            Assert.Equal(count, ChaosSkiaFxOverlay.Active!.Fx.ParticleCount);

            ChaosSkiaFxOverlay.CloseActive();
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            ChaosRunHost.PopText = null;
            BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick();
            var off = Assert.Single(BubbleOverlay.Field.Bubbles);
            BubbleOverlay.Press(off, off.CenterX, off.CenterY);
            Assert.True(off.Popping);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            Assert.Equal(2, state.Detonated);
            Assert.Equal(1, fires);
            Assert.Equal(1, state.EffectsFired);
            Assert.Equal(2, state.Combo);

            BubbleOverlay.Field.Bubbles.Clear();
            ChaosRunHost.SpawnTick();
            var denied = Assert.Single(BubbleOverlay.Field.Bubbles);
            state.Focus = 20;
            double beforeScore = state.Score, beforeFocus = state.Focus;
            int beforeGold = ChaosMeta.State.Gold;
            loc.SetLanguage("es");
            CoreSettings.Current.ChaosAnnouncerEnabled = true;
            ChaosAnnouncerOverlay.CloseActive();
            var words = new System.Collections.Generic.List<string>();
            ChaosRunHost.PopText = (_, _, word, _) => words.Add(word);
            for (int i = 0; i < 188; i++) BubbleOverlay.Field.Step();
            ChaosRunHost.PopText = null;
            Assert.Contains("DENEGADA", words);
            Assert.True(denied.Popping);
            Assert.True(state.Score > beforeScore);
            Assert.Equal(beforeFocus + ChaosTuning.FOCUS_PER_DENIED, state.Focus);
            Assert.True(ChaosMeta.State.Gold > beforeGold);
            var reward = $"+{ChaosMeta.State.Gold - beforeGold} {ChaosGlyphs.Gold}";
            Assert.Equal($"DENEGADA. {reward}", state.RecentEvents[0]);
            Assert.Equal(state.RecentEvents[0], ChaosAnnouncerOverlay.Active!.Current!.Text);
            Assert.Equal(reward, ChaosAnnouncerOverlay.Active!.Current!.SubText);
            Assert.Contains("tease", ChaosMeta.State.BubbleHintsLearned);
            Assert.Equal(1, fires);

            // WPF ChaosModeService.cs:1349 -> :2275: an unshielded touch takes the detonation path.
            var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            BubbleOverlay.Field.Clock = () => now;
            EffectPayload? fired = null;
            EffectPayload.Firer = p => { fires++; fired = p; };
            void Touch(EffectPayload payload)
            {
                state.Shields = 0;
                var t = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { VariantId = "tease", Payload = payload, IsTease = true, SizePx = 100 });
                BubbleOverlay.Press(t, t.CenterX, t.CenterY);
                Assert.True(t.Popping);
            }
            Assert.True(state.Config.AmbientMode); // WPF :316-326, every run is Free Desktop
            var link = new HtLinkPayload { Strength = 40 };
            Touch(link); // Ambient remap: zero roll and no heavy effect = rain
            Assert.Equal(2, fires);
            Assert.Equal(40, Assert.IsType<GifCascadePayload>(fired).Strength);
            var rain = new GifCascadePayload();
            Touch(rain); // the remapped rain holds the one-heavy gate
            Assert.Equal(2, fires);
            Assert.Equal(3, state.EffectsFired);
            Assert.Contains("fizzles", state.RecentEvents[0]);
            Touch(link); // remap under a heavy effect lands on text
            Assert.Equal(3, fires);
            Assert.IsType<BouncingTextPayload>(fired);
            now = now.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
            Touch(rain);
            Assert.Equal(4, fires);
            Assert.Same(rain, fired);
        }
        finally
        {
            BubbleOverlay.Field.Clock = clock;
            loc.SetLanguage(language);
            ChaosRunHost.ForceShutdown();
            CorePaths.EffectiveAssetsProvider = assetProvider;
            System.IO.Directory.Delete(assets, true);
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.Composited = composited;
            CoreSettings.Current.ChaosSkiaFxEnabled = enabled; CoreSettings.Current.PerformanceMode = performanceMode;
            CoreSettings.Current.ChaosAnnouncerEnabled = announcerEnabled;
            ChaosRunHost.Rng = rng;
            ChaosRunHost.PopText = popText;
            EffectPayload.Firer = firer;
            BubbleOverlay.Image = image;
            ChaosMeta.State.RunsCompleted = runs;
            Assert.Empty(BubbleOverlay.TeaseFaces);
        }
    });
}
