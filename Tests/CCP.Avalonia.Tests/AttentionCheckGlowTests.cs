using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AttentionCheckGlowTests
{
    [Fact]
    public void GlowTracksTheVisibleArcInsteadOfLightingTheWholeCircle() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var control = new AttentionCheckControl();
        var glow = control.FindControl<Image>("DotRingGlow");
        Assert.NotNull(glow);
        // The glow is rasterized only once attached, at the top level's density.
        var window = new Window { Width = 180, Height = 180, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            control.SetProgress(0);
            using (var pixels = Pixels(glow))
            {
                Assert.Equal((byte)0, pixels.GetPixel(pixels.Width / 2, pixels.Height / 2 + 40).Alpha);
                Assert.True(pixels.GetPixel(pixels.Width / 2 + 40, pixels.Height / 2).Alpha > 5);
            }

            control.SetProgress(0.5);
            using (var pixels = Pixels(glow))
            {
                int center = pixels.Width / 2;
                Assert.True(pixels.GetPixel(center, center + 40).Alpha > 10);
                Assert.Equal((byte)0, pixels.GetPixel(center, center - 40).Alpha);
                Assert.Equal((byte)0, pixels.GetPixel(center, center).Alpha);
            }
            control.SetProgress(1);
            using (var pixels = Pixels(glow))
            {
                int center = pixels.Width / 2;
                Assert.True(pixels.GetPixel(center, center + 40).Alpha > 10);
                Assert.True(pixels.GetPixel(center, center - 40).Alpha > 10);
                Assert.Equal((byte)0, pixels.GetPixel(center, center).Alpha);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void GlowSharesTheRingPulseAndRebuildsAfterDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var control = new AttentionCheckControl();
        var glow = control.FindControl<Image>("DotRingGlow");
        Assert.NotNull(glow);
        var ring = control.FindControl<Ellipse>("DotRingFg")!;
        Assert.Same(ring.RenderTransform, glow.RenderTransform);
        var window = new Window { Width = 180, Height = 180, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            control.SetProgress(0.5);
            var drawn = Assert.IsType<Bitmap>(glow.Source);
            control.SetProgress(0.5);
            Assert.Same(drawn, glow.Source);
            var ringCenter = ring.TranslatePoint(new Point(42, 42), control)!.Value;
            var glowCenter = glow.TranslatePoint(new Point(56, 56), control)!.Value;
            Assert.Equal(ringCenter.X, glowCenter.X, precision: 10);
            Assert.Equal(ringCenter.Y, glowCenter.Y, precision: 10);
            window.Content = null;
            Assert.Null(glow.Source);
            window.Content = control;
            Dispatcher.UIThread.RunJobs();
            using var pixels = Pixels(glow);
            Assert.True(pixels.GetPixel(pixels.Width / 2, pixels.Height / 2 + 40).Alpha > 10);
        }
        finally { window.Close(); }
    });

    private static SKBitmap Pixels(Image glow)
    {
        using var stream = new MemoryStream();
        Assert.IsType<Bitmap>(glow.Source).Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
