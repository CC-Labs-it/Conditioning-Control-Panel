using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Super Whirlpool on this head: the pool rides the spiral windows, the base spiral steps
/// aside behind its veil, and the gate, panic and the restless click are Core's rules.</summary>
public sealed class WhirlpoolOverlayTests
{
    // 4x4, three frames, 30 ms each (SpiralOverlayTests.TinyGif).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private static global::Avalonia.Platform.Screen Screen()
    {
        var s = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(s, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(s, true);
        return s;
    }

    /// <summary>A spiral on screen (one 800x600 monitor) with the given account, then the test body.</summary>
    private static async Task WithSpiral(bool premium, Func<ConditioningControlPanel.Models.AppSettings, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-whirlpool-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                // Seed first: the seams it sets are what the finally block puts back.
                SteamOverlay.Seed();
                WhirlpoolOverlay.Seed();
                var provider = CoreSettings.ServiceProvider;
                var settings = new ConditioningControlPanel.Services.SettingsService();
                var saved = (CoreEntitlement.HasPremiumProvider, CoreSession.IsEngineRunningProvider, SpiralOverlay.SkipPlatformChecksForTests,
                    ScreenList.QueryOverride, SuperStageHost.CursorProvider, SuperStageHost.ClickProvider, PinkFilterOverlay.RaiseWindow);
                var host = new Window();
                try
                {
                    if (SpiralOverlay.Decoding is { } prior) await prior;
                    SpiralOverlay.CloseAll();
                    SuperStageHost.Stop();
                    CoreSettings.ServiceProvider = () => settings;
                    var s = settings.Current;
                    s.GentleVisuals = false;
                    s.PinkFilterEnabled = false;   // no fog: the pool's own clock steps the stage
                    s.SpiralEnabled = true; s.SpiralPath = gif; s.SpiralRandomize = false;
                    CoreEntitlement.HasPremiumProvider = () => premium;
                    CoreSession.IsEngineRunningProvider = () => true;
                    SpiralOverlay.SkipPlatformChecksForTests = true;
                    ScreenList.QueryOverride = _ => new[] { Screen() };
                    PinkFilterOverlay.RaiseWindow = _ => true;
                    SuperStageHost.CursorProvider = () => (400, 300);   // never the desk's real mouse
                    host.Show();
                    SpiralOverlay.Refresh(host);
                    if (SpiralOverlay.Decoding is { } first) await first;
                    Assert.True(SpiralOverlay.IsShowing);
                    Assert.False(SteamOverlay.On);
                    await body(s);
                }
                finally
                {
                    SuperStageHost.Stop();
                    // A step copies the test profile's options into the shared pool: put back the stock ones.
                    SuperStageHost.Stage.Pool.Restless = false;
                    SpiralOverlay.CloseAll();
                    host.Close();
                    (CoreEntitlement.HasPremiumProvider, CoreSession.IsEngineRunningProvider, SpiralOverlay.SkipPlatformChecksForTests,
                        ScreenList.QueryOverride, SuperStageHost.CursorProvider, SuperStageHost.ClickProvider, PinkFilterOverlay.RaiseWindow) = saved;
                    CoreSettings.ServiceProvider = provider;
                }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    private static bool DrewSomething(WhirlpoolCanvas canvas)
    {
        using var surface = SKSurface.Create(new SKImageInfo(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        if (!canvas.Draw(surface.Canvas)) return false;
        using var img = surface.Snapshot();
        using var px = SKBitmap.FromImage(img);
        return px.GetPixel(400, 300).Alpha > 0;
    }

    [Fact]
    public Task AFreeAccountSeesNoPoolAndKeepsItsSpiral() => WithSpiral(premium: false, _ =>
    {
        Assert.False(SuperStageHost.Stage.PoolOn);
        Assert.False(WhirlpoolOverlay.On);
        var w = Assert.Single(SpiralOverlay.Shown);
        Assert.Null(w.Pool);
        Assert.Equal(1, SpiralOverlay.Veil);
        Assert.True(w.Spiral.Opacity > 0);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ATierLightsThePoolStepsTheBaseAsideAndClears() => WithSpiral(premium: true, _ =>
    {
        var stage = SuperStageHost.Stage;
        Assert.True(stage.PoolOn);
        Assert.True(WhirlpoolOverlay.On);
        Assert.True(WhirlpoolOverlay.IsTicking);
        var w = Assert.Single(SpiralOverlay.Shown);
        var canvas = Assert.IsType<WhirlpoolCanvas>(w.Pool);
        // A fresh show snaps behind its veil: the base spiral never flashes before the pool.
        Assert.Equal(0, SpiralOverlay.Veil);
        Assert.Equal(0, w.Spiral.Opacity);

        // A pool at nothing draws nothing; one that rested a while draws at the cursor.
        Assert.False(canvas.Draw(SKSurface.Create(new SKImageInfo(8, 8)).Canvas));
        for (int i = 0; i < 300; i++) stage.Pool.Step(1 / 30.0, 400, 300, 0, 1, 600);
        Assert.True(DrewSomething(canvas));

        // The tier lapses: the pool goes at once and the spiral fades back in.
        CoreEntitlement.HasPremiumProvider = () => false;
        SuperStageHost.Sync();
        Assert.False(stage.PoolOn);
        Assert.False(WhirlpoolOverlay.On);
        Assert.Null(w.Pool);
        for (int i = 0; i < 40 && !SpiralOverlay.VeilSettled; i++) WhirlpoolOverlay.Frame(1 / 30.0);
        Assert.Equal(1, SpiralOverlay.Veil);
        Assert.True(w.Spiral.Opacity > 0);
        Assert.False(WhirlpoolOverlay.IsTicking);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicTakesThePoolDownAtOnce() => WithSpiral(premium: true, _ =>
    {
        var stage = SuperStageHost.Stage;
        Assert.True(WhirlpoolOverlay.On);
        for (int i = 0; i < 300; i++) stage.Pool.Step(1 / 30.0, 400, 300, 0, 1, 600);
        PanicSurfaces.All.Single(x => x.Id == "super-stage").Stop(null);
        Assert.False(stage.PoolOn);
        Assert.Equal(0, stage.Pool.Size);
        Assert.False(WhirlpoolOverlay.On);
        Assert.All(SpiralOverlay.Shown, w => Assert.Null(w.Pool));
        PanicSurfaces.All.Single(x => x.Id == "voice-overlays").Stop(null);
        Assert.False(SpiralOverlay.IsShowing);
        Assert.False(stage.PoolOn);
        Assert.False(WhirlpoolOverlay.IsTicking);
        Assert.Equal(1, SpiralOverlay.Veil);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AClickKicksARestlessPool() => WithSpiral(premium: true, async s =>
    {
        s.WhirlpoolRestless = true;
        bool tapped = false;
        SuperStageHost.ClickProvider = () => (false, tapped);
        var pool = SuperStageHost.Stage.Pool;
        // The first steps only start the clock and clear an old press.
        for (int i = 0; i < 3; i++) { WhirlpoolOverlay.Frame(1 / 30.0); await Task.Delay(15); }
        double before = pool.Size;
        Assert.Equal(0, before);   // a restless pool at rest calms to nothing
        tapped = true;
        // The pool's own timer also steps; a step inside 4 ms of the last one waits, so allow a few.
        for (int i = 0; i < 10 && pool.Size <= before; i++) { await Task.Delay(10); WhirlpoolOverlay.Frame(1 / 30.0); }
        Assert.True(pool.Size - before > Whirlpool.ClickKick * 0.5, $"{before} -> {pool.Size}");
    });

    [Fact]
    public Task ThePoolIsRaisedOverTheFog() => WithSpiral(premium: true, _ =>
    {
        var raised = new List<Window>();
        PinkFilterOverlay.RaiseWindow = w => { raised.Add(w); return true; };
        WhirlpoolOverlay.RaisePool();
        Assert.Equal(SpiralOverlay.Shown.Cast<Window>(), raised);
        return Task.CompletedTask;
    });

    [Fact]
    public void TheStarShowsAndTheRenderSampleDraws() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Contains(SuperEffect.Vortex, SuperBox.HostedOnThisHead);
        var sample = (WhirlpoolCanvas)Activator.CreateInstance(typeof(WhirlpoolCanvas), nonPublic: true)!;
        // The stage is off; the sample brings its own rested pool, so the proof shows the picture.
        Assert.False(SuperStageHost.Stage.PoolOn);
        using var surface = SKSurface.Create(new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        Assert.True(sample.Draw(surface.Canvas));
        using var px = SKBitmap.FromImage(surface.Snapshot());
        Assert.True(px.GetPixel(640, 360).Alpha > 0);
        Assert.Equal(0, px.GetPixel(5, 5).Alpha);
    });
}
