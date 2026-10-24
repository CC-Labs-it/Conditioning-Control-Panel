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
        Assert.Equal(2, visuals.OfType<Border>().Count(border => border.BoxShadow.Count > 0));
        // WPF's ring glow is the ring's own effect: invisible when nothing is filled.
        Assert.Equal(expected, control.FindControl<Border>("DotRingGlow")!.Opacity, 8);
    });

    [Fact]
    public void RingGlowStartsDark() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        Assert.Equal(0, new AttentionCheckControl().FindControl<Border>("DotRingGlow")!.Opacity);
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
        var glowScale = Assert.IsType<ScaleTransform>(control.FindControl<Border>("DotRingGlow")!.RenderTransform);
        var window = new Window { Width = 150, Height = 150, Content = control };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
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
