using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AttentionCheckControlTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void ProgressFillsClockwiseAndClamps(double input, double expected) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var control = new AttentionCheckControl();
        control.SetProgress(input);
        var ring = control.FindControl<Ellipse>("DotRingFg")!;
        double units = Math.PI * 80 / 4;
        Assert.Equal(expected * units, ring.StrokeDashArray![0], 8);
        Assert.Equal(Math.Max(0.001, (1 - expected) * units), ring.StrokeDashArray[1], 8);
        var group = Assert.IsType<TransformGroup>(ring.RenderTransform);
        Assert.Equal(-90, group.Children.OfType<RotateTransform>().Single().Angle);
        Assert.False(control.IsHitTestVisible);
        var visuals = Assert.IsType<Grid>(control.Content).Children;
        Assert.DoesNotContain(visuals, visual => visual.Effect != null);
        Assert.Equal(2, visuals.OfType<Border>().Count(border => border.BoxShadow.Count > 0)
            + visuals.OfType<Image>().Count(image => image.Name == "DotRingGlow"));
        // Nothing is rasterized before attach, even after SetProgress: the density is unknown until then.
        Assert.Null(control.FindControl<Image>("DotRingGlow")!.Source);
        Assert.Same(group, control.FindControl<Image>("DotRingGlow")!.RenderTransform);
        Assert.Equal(1, visuals.OfType<Border>().Count(border => border.BoxShadow.Count > 0));
        Assert.Equal(expected, control.FindControl<Image>("DotRingGlow")!.Opacity, 8);
    });

    [Fact]
    public void RingGlowStartsDark() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        Assert.Equal(0, new AttentionCheckControl().FindControl<Image>("DotRingGlow")!.Opacity);
    });

    [Fact]
    public void PulseUsesSharedBeatAndStopsOnDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        BeatLoop.Time = time;
        var control = new AttentionCheckControl();
        var ring = control.FindControl<Ellipse>("DotRingFg")!;
        var scale = ((TransformGroup)ring.RenderTransform!).Children.OfType<ScaleTransform>().Single();
        var glowTransform = Assert.IsType<TransformGroup>(control.FindControl<Image>("DotRingGlow")!.RenderTransform);
        var glowScale = Assert.Single(glowTransform.Children.OfType<ScaleTransform>());
        var window = new Window { Width = 150, Height = 150, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var glow = control.FindControl<Image>("DotRingGlow")!;
            // Attach rasterizes the glow at the top level's density (headless = 1): 84 + 2*14 px.
            var bitmap = Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(glow.Source);
            Assert.Equal(new PixelSize(112, 112), bitmap.PixelSize);
            control.StartPulse();
            var pulse = Assert.Single(BeatLoop.Running.Where(p => p.Owner == control));
            Assert.True(pulse.OnFrameBeat);
            time.Advance(TimeSpan.FromMilliseconds(210));
            BeatLoop.StepAll();
            Assert.Equal(1.09, scale.ScaleX, 8);
            Assert.Equal(scale.ScaleX, scale.ScaleY);
            Assert.Equal(scale.ScaleX, glowScale.ScaleX);
            Assert.Equal(scale.ScaleX, glowScale.ScaleY);
            time.Advance(TimeSpan.FromMilliseconds(210));
            BeatLoop.StepAll();
            Assert.Equal(1.18, scale.ScaleX, 8);
            control.IsVisible = false;
            Assert.False(pulse.IsTicking);
            control.IsVisible = true;
            Assert.True(pulse.IsTicking);
            control.StartPulse();
            Assert.Single(BeatLoop.Running.Where(p => p.Owner == control));
            Assert.Equal(1, scale.ScaleX);
            time.Advance(TimeSpan.FromMilliseconds(420));
            BeatLoop.StepAll();
            Assert.Equal(1.18, scale.ScaleX, 8);
            window.Content = null;
            Assert.Null(glow.Source);
            Assert.DoesNotContain(BeatLoop.Running, p => p.Owner == control);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, scale.ScaleY);
            Assert.Equal(1, glowScale.ScaleX);
            control.StopPulse();
        }
        finally
        {
            control.StopPulse();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldTime;
        }
    });
}
