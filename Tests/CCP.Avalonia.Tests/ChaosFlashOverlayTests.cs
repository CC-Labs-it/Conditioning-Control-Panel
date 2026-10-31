using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Chaos braindrain wash (WPF Chaos/ChaosFlashOverlay.cs): one spanning window per surface,
/// the fade on the Core curve through a stepped clock (P08), GIF frames loop, panic takes it down and
/// a late fade step cannot bring it back, a refused platform shows nothing.</summary>
public sealed class ChaosFlashOverlayTests
{
    // 4x4, three frames, 30 ms each, looping (same bytes as SpiralOverlayTests).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private static readonly PixelRect[] TwoScreens = { new(0, 0, 1920, 1080), new(1920, 0, 1280, 1024) };

    [Fact]
    public async Task WashFadesOnTheCurveLoopsTheGifAndPanicClosesIt()
    {
        var gif = WriteGif(out var dir);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var steps = new List<(Action Run, TimeSpan At)>();
            var schedule = ChaosFlashOverlay.Schedule;
            try
            {
                ChaosFlashOverlay.SkipPlatformChecksForTests = true;
                ChaosFlashOverlay.Schedule = (a, t) => steps.Add((a, t));
                await ChaosFlashOverlay.ShowPath(TwoScreens, gif, 4500, 0.30);

                Assert.Single(ChaosFlashOverlay.Shown);
                Assert.Equal(new PixelPoint(0, 0), ChaosFlashOverlay.Shown[0].Position);
                Assert.Equal(3200, ChaosFlashOverlay.Shown[0].Width);
                Assert.True(ChaosFlashOverlay.IsAnimating);
                Assert.Equal(0, ChaosFlashOverlay.Alpha);
                var first = ChaosFlashOverlay.Shown[0].Spiral.Source;
                ChaosFlashOverlay.NextFrame();
                Assert.NotSame(first, ChaosFlashOverlay.Shown[0].Spiral.Source);

                // Fade-in lands on the peak at 500 ms; the last step is the close at 4500 + 700.
                Assert.Equal(TimeSpan.FromMilliseconds(5200), steps[^1].At);
                foreach (var s in steps) if (s.At <= TimeSpan.FromMilliseconds(500)) s.Run();
                Assert.Equal(0.30, ChaosFlashOverlay.Alpha, 10);

                // Panic: down now, and the pending fade steps are dead.
                PanicSurfaces.StopAll("chaos-flash test");
                Assert.False(ChaosFlashOverlay.IsShowing || ChaosFlashOverlay.IsAnimating);
                foreach (var s in steps.Take(steps.Count - 1)) s.Run();
                Assert.False(ChaosFlashOverlay.IsShowing);
                Assert.Equal(0, ChaosFlashOverlay.Alpha);

                // A newer wash is not closed by the old wash's last step; left alone it closes on its own.
                var stale = steps[^1];
                steps.Clear();
                await ChaosFlashOverlay.ShowPath(TwoScreens, gif, 600, 0.10);
                stale.Run();
                Assert.True(ChaosFlashOverlay.IsShowing);
                foreach (var s in steps.ToArray()) s.Run();
                Assert.False(ChaosFlashOverlay.IsShowing);
            }
            finally
            {
                ChaosFlashOverlay.CloseActive();
                ChaosFlashOverlay.SkipPlatformChecksForTests = false;
                ChaosFlashOverlay.Schedule = schedule;
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public Task StillUsesStageWidthAndACompletedWashReusesItsWindow() => WithImage(async (gif, steps) =>
    {
        var path = Path.ChangeExtension(gif, ".png");
        using (var bitmap = new SkiaSharp.SKBitmap(3000, 1000))
        using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            File.WriteAllBytes(path, data.ToArray());
        var raised = new List<global::Avalonia.Controls.TopLevel>();
        X11Overlay.RaiseRequestedForTests = w =>
        {
            Assert.True(Assert.IsAssignableFrom<global::Avalonia.Controls.Window>(w).IsVisible);
            raised.Add(w);
        };
        await ChaosFlashOverlay.ShowPath(TwoScreens, path, 600, .1);
        var window = Assert.Single(ChaosFlashOverlay.Shown);
        Assert.Same(window, Assert.Single(raised));
        Assert.Equal(2560, ((global::Avalonia.Media.Imaging.Bitmap)window.Spiral.Source!).PixelSize.Width);
        Assert.False(ChaosFlashOverlay.IsAnimating);
        foreach (var step in steps.ToArray()) step.Run();
        Assert.False(ChaosFlashOverlay.IsShowing);
        Assert.Null(window.Spiral.Source);
        Assert.Contains(window, ChaosFlashOverlay.Shown);
        Assert.True(window.IsVisible);
        Assert.Equal(TimeSpan.FromSeconds(8), steps.Last().At);
        var hide = steps.Last().Run;
        hide();
        Assert.False(window.IsVisible);
        steps.Clear();
        await ChaosFlashOverlay.ShowPath(TwoScreens, path, 600, .1);
        Assert.Same(window, Assert.Single(ChaosFlashOverlay.Shown));
        Assert.Equal(2, raised.Count); // retained window raised again after Show
        ChaosFlashOverlay.RaiseActive(); // WPF ChaosFlashOverlay.cs:82
        Assert.Equal(3, raised.Count);
        Assert.All(raised, w => Assert.Same(window, w));
        hide(); // a grace callback from the old wash cannot hide its replacement
        Assert.True(window.IsVisible);
        PanicSurfaces.StopAll("chaos-flash still test");
        Assert.Empty(ChaosFlashOverlay.Shown);
    });

    [Fact]
    public Task HeroIsLocalOnlyStillAndIndependentAndPanicClosesBothSurfaces() => WithImage(async (gif, steps) =>
    {
        var shown = 0;
        await ChaosFlashOverlay.ShowHeroPath(TwoScreens, "https://example.invalid/poster.gif", false, () => shown++);
        Assert.Empty(ChaosFlashOverlay.HeroShown);
        Assert.Equal(0, shown);
        await ChaosFlashOverlay.ShowPath(TwoScreens, gif, 600, .1);
        var wash = Assert.Single(ChaosFlashOverlay.Shown);
        var frame = wash.Spiral.Source;
        var acceptedBeforeDecode = false;
        await ChaosFlashOverlay.ShowHeroPath(TwoScreens, gif, true, () =>
        {
            acceptedBeforeDecode = ChaosFlashOverlay.HeroShown.Count == 1 && ChaosFlashOverlay.HeroShown[0].Spiral.Source == null;
            shown++;
        });
        Assert.True(acceptedBeforeDecode);
        Assert.False(ChaosFlashOverlay.IsHeroAnimating);
        var hero = Assert.Single(ChaosFlashOverlay.HeroShown);
        Assert.NotSame(wash, hero);
        Assert.Equal(1, shown);
        Assert.Same(frame, wash.Spiral.Source);
        var heroFrame = hero.Spiral.Source;
        ChaosFlashOverlay.NextFrame();
        Assert.Same(heroFrame, hero.Spiral.Source);
        Assert.NotSame(frame, wash.Spiral.Source);
        ChaosFlashOverlay.StopHero();
        Assert.Null(hero.Spiral.Source);
        Assert.True(ChaosFlashOverlay.IsShowing && ChaosFlashOverlay.IsAnimating);
        await ChaosFlashOverlay.ShowHeroPath(TwoScreens, gif, false);
        Assert.True(ChaosFlashOverlay.IsHeroAnimating);
        Assert.Same(hero, Assert.Single(ChaosFlashOverlay.HeroShown));
        PanicSurfaces.StopAll("chaos-flash hero test");
        Assert.Empty(ChaosFlashOverlay.Shown);
        Assert.Empty(ChaosFlashOverlay.HeroShown);
    });

    [Fact]
    public void StageIncludesNegativeOriginsAndVerticalOffsets()
    {
        Assert.Equal(new PixelRect(-1600, -200, 3520, 1280),
            ChaosFlashOverlay.StageBounds(new[] { new PixelRect(-1600, -200, 1600, 900), new PixelRect(0, 0, 1920, 1080) }));
        Assert.Equal(default, ChaosFlashOverlay.StageBounds(Array.Empty<PixelRect>()));
    }

    [Fact]
    public Task ShowPicksLocalPoolAndUsesPrimaryOrVirtualStage() => WithImage(async (gif, steps) =>
    {
        CoreSettings.Current.GlobalTargetMonitor = 1;
        CoreSettings.Current.DualMonitorEnabled = false;
        ChaosFlashOverlay.Show(new global::Avalonia.Controls.Border());
        await ChaosFlashOverlay.LastShow!;
        var primary = Assert.Single(ChaosFlashOverlay.Shown);
        Assert.Equal(1920, primary.Width);
        Assert.Equal(new PixelPoint(0, 0), primary.Position);
        Assert.NotNull(primary.Spiral.Source);
        CoreSettings.Current.DualMonitorEnabled = true;
        ChaosFlashOverlay.Show(new global::Avalonia.Controls.Border());
        await ChaosFlashOverlay.LastShow!;
        Assert.Equal(3200, Assert.Single(ChaosFlashOverlay.Shown).Width);
    });

    [Fact]
    public void WashDispatcherIgnoresOtherPayloadsAndAnAbsentShell()
    {
        var oldFirer = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        var before = ChaosFlashOverlay.LastShow;
        try
        {
            var hostCalls = 0;
            ConditioningControlPanel.Avalonia.Views.Chaos.ChaosWashPayloads.Seed(
                () => { hostCalls++; return null; });
            new ConditioningControlPanel.Services.Chaos.OverlayPayload("spiral").Fire();
            Assert.Equal(0, hostCalls);
            ConditioningControlPanel.Avalonia.Views.Chaos.ChaosWashPayloads.Seed(() => null);
            new ConditioningControlPanel.Services.Chaos.OverlayPayload("braindrain").Fire();
            Assert.Same(before, ChaosFlashOverlay.LastShow);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = oldFirer; }
    }

    // WPF Services/Chaos/EffectPayloadFiring.cs:50-59. Exercise EffectPayload.Fire, the same
    // seam reached by ChaosRunHost's benign pop (:218) and live detonation (:287).
    [Theory]
    [InlineData(false, 100, 2.0, 3.0, 10700, .10)]
    [InlineData(true, 0, 1.0, 1.0, 2200, .30)]
    [InlineData(true, 50, 2.0, 1.5, 9700, .30)]
    [InlineData(true, 100, 1.0, 1.0, 5200, .30)]
    public Task LiveWashPayloadUsesWpfDefaultsOrScaledCustomWash(
        bool custom, int strength, double global, double mult, int endMs, double peak) => WithImage(async (gif, steps) =>
    {
        CoreSettings.Current.DualMonitorEnabled = true;
        ConditioningControlPanel.Avalonia.Views.Chaos.ChaosWashPayloads.Seed(
            () => new global::Avalonia.Controls.Border());
        ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult = global;
        var payload = new ConditioningControlPanel.Services.Chaos.OverlayPayload(
            "braindrain", custom ? .30 : null) { Strength = strength, DurationMult = mult };
        payload.Fire();
        await ChaosFlashOverlay.LastShow!;
        Assert.NotNull(Assert.Single(ChaosFlashOverlay.Shown).Spiral.Source);
        Assert.Equal(TimeSpan.FromMilliseconds(endMs), steps[^1].At);
        foreach (var step in steps.Where(s => s.At <= TimeSpan.FromMilliseconds(500))) step.Run();
        Assert.Equal(peak, ChaosFlashOverlay.Alpha, 10);
        // The live route retains panic teardown, including already scheduled fade callbacks.
        PanicSurfaces.StopAll("payload test");
        foreach (var step in steps.ToArray()) step.Run();
        Assert.Empty(ChaosFlashOverlay.Shown);
        Assert.Equal(0, ChaosFlashOverlay.Alpha);
    });

    [Fact]
    public Task PanicOrphansAnInFlightDecode() => WithImage(async (gif, steps) =>
    {
        var shown = 0;
        var heroBefore = ChaosFlashOverlay.LastHeroShow;
        ChaosFlashOverlay.ShowHero(new global::Avalonia.Controls.Border(), gif, 600, .1, false, () => shown++);
        var pending = ChaosFlashOverlay.ShowPath(TwoScreens, gif, 600, .1);
        PanicSurfaces.StopAll("pending chaos-flash test");
        await pending;
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        if (ChaosFlashOverlay.LastHeroShow is { } hero && hero != heroBefore) await hero;
        Assert.Equal(0, shown);
        Assert.Empty(ChaosFlashOverlay.HeroShown);
        Assert.Empty(ChaosFlashOverlay.Shown);
    });

    [Fact]
    public void DecodeKeepsAtMostMaxFramesStridedAcrossTheAnimation()
    {
        var gif = WriteGif(out var dir);
        try
        {
            var (frames, delay) = SpiralOverlay.Decode(gif, 0, maxFrames: 2);   // 3 frames x 30 ms -> every 2nd
            Assert.Equal(2, frames.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(60), delay);
            foreach (var f in frames) f.Dispose();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task APlatformWithoutClickThroughShowsNothing()
    {
        var gif = WriteGif(out var dir);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var schedule = ChaosFlashOverlay.Schedule;
            try
            {
                ChaosFlashOverlay.Schedule = (_, _) => { };
                await ChaosFlashOverlay.ShowPath(TwoScreens, gif, 4500, 0.30);   // headless windows: no X11 click-through
                Assert.False(ChaosFlashOverlay.IsShowing || ChaosFlashOverlay.IsAnimating);
                var shown = 0;
                await ChaosFlashOverlay.ShowHeroPath(TwoScreens, gif, false, () => shown++);
                Assert.Empty(ChaosFlashOverlay.HeroShown);
                Assert.Equal(0, shown);
            }
            finally
            {
                ChaosFlashOverlay.CloseActive();
                ChaosFlashOverlay.ResetRefusalForTests();
                ChaosFlashOverlay.Schedule = schedule;
                Directory.Delete(dir, true);
            }
        });
    }

    // Shared UI-thread setup and teardown. Every behavioral assertion stays in its test.
    private static async Task WithImage(Func<string, List<(Action Run, TimeSpan At)>, Task> test)
    {
        var gif = WriteGif(out var dir);
        Directory.CreateDirectory(Path.Combine(dir, "images"));
        var pooled = Path.Combine(dir, "images", "tiny.gif");
        File.Move(gif, pooled);
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                Setup();
                var oldAssets = CorePaths.EffectiveAssetsProvider;
                var oldQuery = ConditioningControlPanel.Avalonia.Views.Features.ScreenList.QueryOverride;
                var oldFirer = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
                var oldGlobal = ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult;
                var oldRaise = X11Overlay.RaiseRequestedForTests;
                var schedule = ChaosFlashOverlay.Schedule;
                try
                {
                    CorePaths.EffectiveAssetsProvider = () => dir;
                    ConditioningControlPanel.Avalonia.Views.Features.ScreenList.QueryOverride = _ =>
                        TwoScreens.Select((b, i) => MultiMonitorResultTests.MakeScreen(1, b, b, i == 0)).ToArray();
                    ChaosFlashOverlay.SkipPlatformChecksForTests = true;
                    var steps = new List<(Action Run, TimeSpan At)>();
                    ChaosFlashOverlay.Schedule = (a, t) => steps.Add((a, t));
                    await test(pooled, steps);
                }
                finally
                {
                    ChaosFlashOverlay.CloseActive();
                    ChaosFlashOverlay.SkipPlatformChecksForTests = false;
                    ChaosFlashOverlay.Schedule = schedule;
                    CorePaths.EffectiveAssetsProvider = oldAssets;
                    ConditioningControlPanel.Avalonia.Views.Features.ScreenList.QueryOverride = oldQuery;
                    ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = oldFirer;
                    ConditioningControlPanel.Services.Chaos.EffectPayload.GlobalDurationMult = oldGlobal;
                    X11Overlay.RaiseRequestedForTests = oldRaise;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    internal static string WriteGif(out string dir)
    {
        dir = Directory.CreateTempSubdirectory("ccp-chaosflash-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        return gif;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
