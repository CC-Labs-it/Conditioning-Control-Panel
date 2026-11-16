using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBackdropTests
{
    [Fact]
    public async Task Story_run_shows_plate_swaps_depth_and_panic_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var narrative = settings.NarrativeModeEnabled;
            var backdrop = settings.BackdropEnabled;
            try
            {
                settings.NarrativeModeEnabled = true;
                settings.BackdropEnabled = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false, PlayMode = ChaosPlayMode.Story });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var plate = Assert.IsType<ChaosBackdropWindow>(ChaosBackdropWindow.Active);
                Assert.True(plate.IsVisible);
                Assert.Equal(1, plate.Depth);
                plate.SetDepth(2);
                Assert.Equal(2, plate.Depth);
                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosBackdropWindow.Active);
                Assert.False(plate.IsVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                settings.NarrativeModeEnabled = narrative;
                settings.BackdropEnabled = backdrop;
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosBackdropService.cs:128-169 (crossfade, idle stop) and :204-293 (Enhanced FX).
    [Fact]
    public async Task Depth_swap_crossfades_and_enhanced_fx_animates()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var (narrative, backdrop, fx, time) = (settings.NarrativeModeEnabled, settings.BackdropEnabled, settings.ChaosSkiaFxEnabled, BeatLoop.Time);
            try
            {
                BeatLoop.Time = new SteppedTime();   // frame beats see no elapsed time: only Step() advances
                ChaosArt.RootsOverride = () => new[] { Path.Combine(AppContext.BaseDirectory, "assets", "Chaos") };
                settings.NarrativeModeEnabled = settings.BackdropEnabled = true;
                settings.ChaosSkiaFxEnabled = false;
                ChaosBackdropWindow.ShowForRun(ChaosPlayMode.Story, 1);
                var w = Assert.IsType<ChaosBackdropWindow>(ChaosBackdropWindow.Active);
                var plate = w.Plate;
                Dispatcher.UIThread.RunJobs();
                Assert.True(plate.HasArt && plate.HasBloom);
                Assert.NotNull(ChaosBackdropWindow.CurrentSource);
                Assert.False(plate.Fading);
                var depth1 = Frame(w);

                w.SetDepth(1);   // same zone: no restart
                Assert.False(plate.Fading);
                w.SetDepth(2);
                Assert.True(plate.Fading);
                Assert.True(plate.Ticking);
                Assert.Equal(0f, plate.FadeT);
                Assert.True(Diff(depth1, Frame(w)) < 50);   // the outgoing plate still shows
                for (int i = 0; i < 7; i++) plate.Step();
                var mid = Frame(w);
                for (int i = 0; i < 7; i++) plate.Step();
                Assert.False(plate.Fading);
                Assert.False(plate.Ticking);   // FX off and not fading: idle
                var depth2 = Frame(w);
                Assert.True(Diff(depth1, depth2) > 1000);
                Assert.True(Diff(mid, depth1) > 1000 && Diff(mid, depth2) > 1000);

                settings.ChaosSkiaFxEnabled = true;
                for (int i = 0; i < 60; i++) plate.Step();
                Assert.True(plate.TwinkleCount > 0);
                Assert.True(Diff(depth2, Frame(w)) > 1000);   // breathing scale, glow and twinkles
                ChaosBackdropWindow.CloseActive();
                Assert.False(plate.HasArt);
            }
            finally
            {
                ChaosBackdropWindow.CloseActive();
                settings.NarrativeModeEnabled = narrative;
                settings.BackdropEnabled = backdrop;
                settings.ChaosSkiaFxEnabled = fx;
                BeatLoop.Time = time;
                ChaosArt.ResetForTest();
            }
            return Task.CompletedTask;
        });
    }

    private static byte[] Frame(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = w.CaptureRenderedFrame()!;
        var size = frame.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var mem = Marshal.AllocHGlobal(bytes.Length);
        try { frame.CopyPixels(new PixelRect(size), mem, bytes.Length, size.Width * 4); Marshal.Copy(mem, bytes, 0, bytes.Length); }
        finally { Marshal.FreeHGlobal(mem); }
        return bytes;
    }

    private static int Diff(byte[] a, byte[] b) =>
        Enumerable.Range(0, a.Length / 4).Count(i => Math.Abs(a[i * 4] - b[i * 4]) + Math.Abs(a[i * 4 + 1] - b[i * 4 + 1]) + Math.Abs(a[i * 4 + 2] - b[i * 4 + 2]) > 12);
}
