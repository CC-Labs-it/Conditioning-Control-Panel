using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Linux beta fix round 3, lane pop (owner, 2026-11-06, WSLg on the CPU, Lighter effects Auto): "the bubble just
/// disappeared when popped". WPF FirstShowEffects.Pop (:307-319) bursts the popped bubble with its ChaosFxLayer
/// (EmitBurst hot pink x1.3 + a strong 500 ms EmitRipple) and plays the "pop" cue. The port now emits into the same
/// Core layer, and the burst is direct feedback: the CPU path's lighter tier keeps it, while Reduced/Off motion,
/// Gentle visuals and the user's own Performance mode drop it, as WPF's MotionFx.AllowParticles does.
/// Every frame here is stepped (Tick at 60 fps), never wall-clock.
/// </summary>
public sealed class FirstShowPopTests(ITestOutputHelper output)
{
    private const double Frame = 1 / 60.0;
    private static readonly double PopAt = S.BubblesAt + .5;   // the first bubble is fully in (alpha 1, entrance scale settled)
    /// <summary>The frames after the pop the shots are taken at (one Tick each: 1/60 s).</summary>
    internal static readonly int[] ShotFrames = { 1, 3, 6, 12, 18, 27, 36 };

    private static string[] Pictures() => Enumerable.Range(0, 4)
        .Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray();

    [Theory]
    [InlineData(LighterEffectsMode.Auto)]
    [InlineData(LighterEffectsMode.On)]
    [InlineData(LighterEffectsMode.Off)]
    public Task APopBurstsOnTheCpuPathWithWpfsLayer(LighterEffectsMode mode) => WithShow(mode, MotionLevel.Full, false, (fx, cues) =>
    {
        bool floor = mode != LighterEffectsMode.Off;
        Assert.Equal(floor, SoftwareRender.FloorActive);
        Assert.Equal(!floor, AmbientFxCanvas.Env.AllowParticles);     // ambient sparks: WPF's Performance tier on the CPU path
        Assert.True(AmbientFxCanvas.Env.AllowFeedbackParticles);       // a click's burst: kept
        var bubble = fx.LiveBubbles.Single();
        int particles = fx.Fx.ParticleCount, ripples = fx.Fx.RippleCount;
        fx.Pop(bubble.X, bubble.Y);
        Assert.Empty(fx.LiveBubbles);                                  // WPF: the bubble goes at once...
        Assert.Equal(new[] { "pop" }, cues);                           // ...with the cue
        // ...and the burst takes its place: flash + ring of 12 (10 + 2 x 1.3) + 7..11 shards, and the ripple's 14 motes.
        Assert.InRange(fx.Fx.ParticleCount - particles, 1 + 12 + 7 + 14, 1 + 12 + 11 + 14);
        Assert.Equal(ripples + 1, fx.Fx.RippleCount);
        if (!floor) return;                                            // with Off the bubble trails keep the field busy
        Assert.Equal(1, fx.Fx.RippleCount);
        int born = fx.Fx.ParticleCount;
        // WPF ChaosFxLayer lives: flash .24 s, ring .34 s, motes .425 s, ripple .5 s, shards .30-.65 s.
        double t = PopAt;
        int Step(int frames) { for (int i = 0; i < frames; i++) { t += Frame; fx.Tick(t, Frame); } return fx.Fx.ParticleCount; }
        Assert.Equal(born, Step(1));
        Assert.InRange(fx.Fx.RippleFront(0), bubble.W / 30 * .9, bubble.W / 30 * 1.1);   // linear front: 1/30 of the width a frame
        Assert.Equal(born, Step(13));                                  // .23 s: everything still flying
        Assert.InRange(fx.Fx.RippleFront(0), bubble.W * .42, bubble.W * .51);
        Assert.Equal(born - 1, Step(2));                               // .27 s: the flash is out
        Assert.InRange(Step(6), born - 1 - 12 - 11, born - 1 - 12);   // .37 s: the ring is out, short shards with it
        Assert.InRange(Step(10), 0, born - 1 - 12 - 14);               // .53 s: the motes are out...
        Assert.Equal(0, fx.Fx.RippleCount);                            // ...and the shock ring
        Assert.Equal(0, Step(8));                                      // .67 s: every shard is out, nothing left running
        Assert.False(fx.Fx.IsActive);
    });

    [Theory]
    [InlineData(MotionLevel.Reduced, false, false)]
    [InlineData(MotionLevel.Off, false, false)]
    [InlineData(MotionLevel.Full, true, false)]    // the user's own Performance mode: WPF drops the burst too
    [InlineData(MotionLevel.Full, false, true)]    // Gentle visuals caps motion at Reduced, as WPF MotionFx.ResolveLevel
    public Task WpfsOwnGatesStillDropTheBurst(MotionLevel motion, bool performance, bool gentle) => WithShow(LighterEffectsMode.Auto, motion, performance, (fx, cues) =>
    {
        CoreSettings.Current.GentleVisuals = gentle;
        Assert.False(AmbientFxCanvas.Env.AllowFeedbackParticles);
        var bubble = fx.LiveBubbles.Single();
        fx.Pop(bubble.X, bubble.Y);
        Assert.Empty(fx.LiveBubbles);
        Assert.Equal(new[] { "pop" }, cues);
        Assert.Equal(0, fx.SparkCount);
    });

    /// <summary>The pop's frames as pixels: rendered on headless Skia (the CPU raster path), each frame is drawn
    /// twice from the same state, with and without the spark field, so the difference is exactly the burst. With
    /// CCP_POP_FRAMES set to a folder the frames are written there (port-{variant}-f{n}.png, a 420 px crop around the
    /// bubble), the same moments ConditioningControlPanel.Tests FirstShowPopShotsTests writes wpf-f{n}.png, plus the
    /// render cost with and without the burst.</summary>
    [Theory]
    [InlineData("cpu-after", true)]
    [InlineData("cpu-before", false)]   // the gate this lane replaced: the CPU path's AllowParticles said no
    public Task ThePopFramesDrawTheBurst(string variant, bool burst) => WithShow(LighterEffectsMode.Auto, MotionLevel.Full, false, (fx, cues) =>
    {
        if (!burst) fx.PopParticles = () => false;
        var dir = Environment.GetEnvironmentVariable("CCP_POP_FRAMES");
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var host = new Window { Width = fx.Width, Height = fx.Height, Content = fx };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var bubble = fx.LiveBubbles.Single();
            var crop = SKRectI.Create((int)Math.Clamp(bubble.X - 210, 0, fx.Width - 420), (int)Math.Clamp(bubble.Y - 210, 0, fx.Height - 420), 420, 420);
            Save(dir, $"port-{variant}-f0.png", Shot(fx, crop, true).Image);
            fx.Pop(bubble.X, bubble.Y);
            double t = PopAt; int at = 0;
            long withMs = 0, withoutMs = 0; int timed = 0;
            foreach (var n in ShotFrames)
            {
                for (; at < n; at++) { t += Frame; fx.Tick(t, Frame); }
                var on = Shot(fx, crop, true); var off = Shot(fx, crop, false);
                for (int r = 0; r < 4; r++)   // best of five: the first render pays JIT and the dot sprite's build
                {
                    var on2 = Shot(fx, crop, true); var off2 = Shot(fx, crop, false);
                    if (on2.Ticks < on.Ticks) { on.Image.Dispose(); on = on2; } else on2.Image.Dispose();
                    if (off2.Ticks < off.Ticks) { off.Image.Dispose(); off = off2; } else off2.Image.Dispose();
                }
                long lit = Lit(on.Image, off.Image);
                output.WriteLine($"{variant} frame {n} ({n * Frame:0.000} s): {fx.SparkCount} sparks, {lit} px changed by the burst, render {Ms(on.Ticks):0.00} ms vs {Ms(off.Ticks):0.00} ms without");
                if (burst && n <= 18) Assert.True(lit > 1500, $"frame {n}: only {lit} px of burst");
                if (!burst) Assert.Equal(0, lit);
                Save(dir, $"port-{variant}-f{n}.png", on.Image);
                withMs += on.Ticks; withoutMs += off.Ticks; timed++;
                on.Image.Dispose(); off.Image.Dispose();
            }
            if (burst)
                output.WriteLine($"CPU raster, 1600x900 stage, mean over {timed} pop frames (best of five each): {Ms(withMs / timed):0.00} ms with the burst, {Ms(withoutMs / timed):0.00} ms without");
        }
        finally { host.Content = null; host.Close(); }
    });

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>One frame of the whole stage on headless Skia, cropped; the spark field on or off.</summary>
    private static (SKBitmap Image, long Ticks) Shot(FirstShowEffects fx, SKRectI crop, bool sparks)
    {
        fx.DrawSparks = sparks;
        try
        {
            long t0 = Stopwatch.GetTimestamp();
            using var rtb = new RenderTargetBitmap(new PixelSize((int)fx.Width, (int)fx.Height));
            rtb.Render(fx);
            long ticks = Stopwatch.GetTimestamp() - t0;
            using var png = new MemoryStream();
            rtb.Save(png);
            using var full = SKBitmap.Decode(png.ToArray());
            var part = new SKBitmap(crop.Width, crop.Height);
            using (var c = new SKCanvas(part))
            {
                c.Clear(new SKColor(0x12, 0x10, 0x1F));   // the desktop the show plays over is dark in the owner's shots
                c.DrawBitmap(full, crop, SKRect.Create(crop.Width, crop.Height));
            }
            return (part, ticks);
        }
        finally { fx.DrawSparks = true; }
    }

    private static long Lit(SKBitmap on, SKBitmap off)
    {
        long n = 0;
        for (int y = 0; y < on.Height; y++)
            for (int x = 0; x < on.Width; x++)
            {
                SKColor a = on.GetPixel(x, y), b = off.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 24) n++;
            }
        return n;
    }

    private static void Save(string? dir, string name, SKBitmap image)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(Path.Combine(dir, name));
        data.SaveTo(f);
    }

    /// <summary>A loaded 1600x900 show on a CPU-detected machine, ticked at 60 fps to <see cref="PopAt"/> with one bubble up.</summary>
    private static Task WithShow(LighterEffectsMode mode, MotionLevel motion, bool performance, Action<FirstShowEffects, System.Collections.Generic.List<string>> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            LifecycleTestPump.Setup();
            var service = new SettingsService();
            var old = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var cues = new System.Collections.Generic.List<string>();
            var fx = new FirstShowEffects(cues.Add, new Random(7)) { Width = 1600, Height = 900 };
            try
            {
                var s = CoreSettings.Current;
                (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode, s.GentleVisuals, s.LighterEffects) = (motion, performance, false, false, mode);
                await fx.LoadAsync(Pictures());
                SoftwareRender.ResetForTests();
                SoftwareRender.Report(true);                            // the probe found CPU Skia (WSLg, a VM)
                for (double t = S.BubblesAt - .2; t < PopAt - Frame / 2; t += Frame) fx.Tick(t, Frame);
                fx.Tick(PopAt, Frame);
                cues.Clear();
                body(fx, cues);
            }
            finally
            {
                SoftwareRender.ResetForTests();
                fx.Dispose(); service.SaveImmediate(); CoreSettings.ServiceProvider = old;
            }
        });
}
