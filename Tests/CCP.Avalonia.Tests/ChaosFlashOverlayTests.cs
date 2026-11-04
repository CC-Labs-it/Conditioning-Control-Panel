using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Chaos braindrain wash (WPF Chaos/ChaosFlashOverlay.cs): one window per stage screen,
/// the fade on the Core curve through a stepped clock (P08), GIF frames loop, panic takes it down and
/// a late fade step cannot bring it back, a refused platform shows nothing.</summary>
public sealed class ChaosFlashOverlayTests
{
    // 4x4, three frames, 30 ms each, looping (same bytes as SpiralOverlayTests).
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private static readonly PixelRect[] TwoScreens = { new(0, 0, 1920, 1080), new(1920, 0, 1280, 1024) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedRunRaisesBothFlashLayersAfterBubbles(bool ambient)
    {
        var gif = WriteGif(out var dir);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var pin = CoreSettings.Current.ChaosPinOnTop;
            var schedule = ChaosFlashOverlay.Schedule;
            var raised = new List<Window>();
            var spawning = typeof(ChaosRunHost).GetField("_spawning", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var wasSpawning = (bool)spawning.GetValue(null)!;
            FlashOverlayWindow? flash = null;
            try
            {
                CoreSettings.Current.ChaosPinOnTop = true;
                ChaosFlashOverlay.SkipPlatformChecksForTests = true;
                ChaosFlashOverlay.Schedule = (_, _) => { };
                ChaosRunHost.RaiseChromeWindow = raised.Add;
                var bubble = new BubbleOverlayWindow();
                ChaosRunHost.RaiseBubbles = () => raised.Add(bubble);
                spawning.SetValue(null, true);
                await ChaosFlashOverlay.ShowPath(TwoScreens, gif, 4500, 0.30);
                flash = new FlashOverlayWindow();
                flash.Show();
                FlashOverlay.Active.Add((flash, new PixelRect(0, 0, 1, 1)));
                ChaosRunHost.KeepChromeTopmost();
                var bubbleIndex = raised.IndexOf(bubble);
                Assert.True(bubbleIndex >= 0);
                Assert.NotEmpty(ChaosFlashOverlay.Shown);
                if (ambient) Assert.True(raised.IndexOf(flash) > bubbleIndex);
                else Assert.All(ChaosFlashOverlay.Shown, w => Assert.True(raised.IndexOf(w) > bubbleIndex));
                if (raised.Contains(ChaosFlashOverlay.Shown[^1]))
                    Assert.True(raised.IndexOf(flash) > raised.IndexOf(ChaosFlashOverlay.Shown[^1]));
                raised.Clear();
                CoreSettings.Current.ChaosPinOnTop = false;
                ChaosRunHost.KeepChromeTopmost();
                Assert.Empty(raised);
            }
            finally
            {
                if (flash != null) { FlashOverlay.Active.RemoveAll(e => e.Window == flash); flash.Close(); }
                ChaosFlashOverlay.CloseActive();
                ChaosFlashOverlay.SkipPlatformChecksForTests = false;
                ChaosFlashOverlay.Schedule = schedule;
                spawning.SetValue(null, wasSpawning);
                ChaosRunHost.RaiseChromeWindow = w => X11Overlay.Raise(w);
                ChaosRunHost.RaiseBubbles = () => BubbleOverlay.RaiseAll(ChaosRunHost.RaiseChromeWindow);
                CoreSettings.Current.ChaosPinOnTop = pin;
                Directory.Delete(dir, true);
            }
        });
    }

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

                Assert.Equal(2, ChaosFlashOverlay.Shown.Count);
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
                foreach (var s in steps) s.Run();
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
