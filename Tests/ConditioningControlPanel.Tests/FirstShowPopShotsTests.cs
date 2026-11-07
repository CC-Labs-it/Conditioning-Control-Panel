using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.FirstShow;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The WPF half of the first-show pop shots (Linux beta fix3, lane pop): with CCP_POP_FRAMES set to a folder, WPF's
/// own FirstShowEffects pops its first bubble at 19.5 s and the stage is written there as wpf-f{n}.png, a 420 px crop
/// around the bubble on the same dark backdrop, at the frames CCP.Avalonia.Tests FirstShowPopTests writes port-*.png.
/// The spark layer is swapped for one at stage scale 1 with the port's seed, so both heads draw at 1 px per DIP.
/// A look check for the port, not a judge. Does nothing otherwise.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FirstShowPopShotsTests
{
    private static readonly int[] ShotFrames = { 1, 3, 6, 12, 18, 27, 36 };

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_POP_FRAMES");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var fx = new FirstShowEffects(_ => { });
        try
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FirstShowEffects).GetField("_particles", flags)!.SetValue(fx, new ChaosFxLayer(() => { }, () => 1f, new Random(7)));
            var pictures = Enumerable.Range(0, 4).Select(i => Path.Combine(RepoRoot(), "Assets", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp")).ToArray();
            var load = fx.LoadAsync(pictures);
            var frame = new DispatcherFrame();
            load.ContinueWith(_ => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            load.GetAwaiter().GetResult();

            const double dt = 1 / 60.0;
            double popAt = FirstShowScript.BubblesAt + .5, t = FirstShowScript.BubblesAt - .2;
            for (; t < popAt - dt / 2; t += dt) fx.Tick(t, dt);
            t = popAt; fx.Tick(t, dt);
            var actors = (IList)typeof(FirstShowEffects).GetField("_actors", flags)!.GetValue(fx)!;
            var bubble = actors.Cast<object>().Single(a => a.GetType().GetField("Bubble")!.GetValue(a) != null);
            double Get(string f) => (double)bubble.GetType().GetField(f)!.GetValue(bubble)!;
            double x = Get("X"), y = Get("Y");
            var crop = SKRectI.Create((int)Math.Clamp(x - 210, 0, fx.Width - 420), (int)Math.Clamp(y - 210, 0, fx.Height - 420), 420, 420);
            Shot(fx, crop, Path.Combine(dir, "wpf-f0.png"));
            fx.Pop(x, y);
            int at = 0;
            foreach (var n in ShotFrames)
            {
                for (; at < n; at++) { t += dt; fx.Tick(t, dt); }
                Shot(fx, crop, Path.Combine(dir, $"wpf-f{n}.png"));
            }
        }
        finally { fx.Dispose(); }
    });

    private static void Shot(FirstShowEffects fx, SKRectI crop, string path)
    {
        using var stage = SKSurface.Create(new SKImageInfo(fx.Width, fx.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        stage.Canvas.Clear(SKColors.Transparent);
        fx.Render(stage.Canvas, fx.Width, fx.Height);
        using var full = stage.Snapshot();
        using var part = SKSurface.Create(new SKImageInfo(crop.Width, crop.Height));
        part.Canvas.Clear(new SKColor(0x12, 0x10, 0x1F));
        part.Canvas.DrawImage(full, crop, SKRect.Create(crop.Width, crop.Height));
        using var img = part.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(path);
        data.SaveTo(f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
