using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using SkiaSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TeaseReviewTests
{
    [Theory]
    [InlineData("flash"), InlineData("sub"), InlineData("pink"), InlineData("spiral"), InlineData("wash"), InlineData("rain")]
    public Task Tease_payloads_reach_the_production_dispatcher(string kind) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var firer = EffectPayload.Firer;
        var flash = AvApp.FireChaosFlash;
        var show = CoreSubliminal.ShowProvider;
        var pool = CoreSettings.Current.SubliminalPool;
        var pinkChecks = PinkFilterOverlay.SkipPlatformChecksForTests;
        var spiralChecks = SpiralOverlay.SkipPlatformChecksForTests;
        var washChecks = ChaosFlashOverlay.SkipPlatformChecksForTests;
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            EffectPayload.Firer = AvApp.FireChaosPayload;
            ChaosRunHost.State!.Shields = 0;
            void Touch(EffectPayload payload) => BubbleOverlay.Press(BubbleOverlay.SpawnChaos(
                new EffectBubbleSpec { IsTease = true, SizePx = 200, Payload = payload }), 0, 0);
            (int?, int?, int?) sent = default;
            AvApp.FireChaosFlash = (_, amount, duration, size, _) => sent = (amount, duration, size);
            var p = new FlashPayload { Strength = 50 };
            if (kind == "flash") { Touch(p); var expected = p.ResolveBurst(CoreSettings.Current);
                Assert.Equal(((int?)expected.Amount, (int?)expected.DurationMs, (int?)expected.Size), sent); }
            string? phrase = null;
            CoreSettings.Current.SubliminalPool = new Dictionary<string, bool> { ["test phrase"] = true };
            CoreSubliminal.ShowProvider = value => phrase = value;
            if (kind == "sub") { Touch(new SubliminalPayload()); Assert.Equal("test phrase", phrase); }
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            SpiralOverlay.SkipPlatformChecksForTests = true;
            if (kind == "pink") { Touch(new OverlayPayload("pink_filter") { Strength = 50 });
                Assert.Equal(.47, PinkFilterOverlay.PaintedOpacity, 3); }
            if (kind == "spiral") { Touch(new OverlayPayload("spiral") { Strength = 50 });
                Assert.Equal(SpiralFrames.Paint(.47), SpiralOverlay.PaintedOpacity, 3); }
            ChaosFlashOverlay.SkipPlatformChecksForTests = true;
            var oldWash = ChaosFlashOverlay.LastShow;
            if (kind == "wash") { Touch(new OverlayPayload("braindrain") { Strength = 50 });
                Assert.NotSame(oldWash, ChaosFlashOverlay.LastShow); }
            var oldRain = ChaosGifCascadeOverlay.LastPick;
            if (kind == "rain") { Touch(new GifCascadePayload()); Assert.NotSame(oldRain, ChaosGifCascadeOverlay.LastPick); }
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); PinkFilterOverlay.Stop(); SpiralOverlay.Stop();
            EffectPayload.Firer = firer; AvApp.FireChaosFlash = flash; CoreSubliminal.ShowProvider = show;
            CoreSettings.Current.SubliminalPool = pool;
            PinkFilterOverlay.SkipPlatformChecksForTests = pinkChecks;
            SpiralOverlay.SkipPlatformChecksForTests = spiralChecks;
            ChaosFlashOverlay.SkipPlatformChecksForTests = washChecks;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0), InlineData(1), InlineData(2)]
    public Task Tease_sprite_and_oversized_face_use_bounded_still(int stage) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (stage == 0) { Assert.NotNull(ChaosArt.Resolve("bubbles", "tease")); return; }
        var dir = Path.Combine(Path.GetTempPath(), "ccp-tease-" + Guid.NewGuid().ToString("N"));
        var pool = Path.Combine(dir, "teasebubble");
        Directory.CreateDirectory(pool);
        try
        {
            var path = Path.Combine(pool, "face.png");
            using (var pixels = new SKBitmap(4100, 10))
            using (var image = SKImage.FromBitmap(pixels))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, data.ToArray());
            Assert.Equal(path, BubbleOverlay.PickTeaseFace(dir, new Random(1)));
            if (stage == 1)
            {
                var (frames, _, animated) = BubbleOverlay.DecodeTeaseFace(path, 64, true);
                try { Assert.False(animated); Assert.Equal(64, Assert.Single(frames).PixelSize.Width); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return;
            }
            var assets = CorePaths.EffectiveAssetsProvider;
            try
            {
                CorePaths.EffectiveAssetsProvider = () => dir;
                var bubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200 });
                for (int i = 0; i < 200 && !BubbleOverlay.TeaseFaces.ContainsKey(bubble); i++) await Task.Delay(10);
                Assert.True(BubbleOverlay.TeaseFaces.ContainsKey(bubble));
                BubbleOverlay.EndChaos();
                Assert.DoesNotContain(bubble, BubbleOverlay.TeaseFaces.Keys);
            }
            finally { BubbleOverlay.EndChaos(); CorePaths.EffectiveAssetsProvider = assets; }
        }
        finally { Directory.Delete(dir, true); }
    });
}
