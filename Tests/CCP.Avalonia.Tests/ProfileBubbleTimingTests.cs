using System;
using System.Linq;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Sample the production tracks at WPF's key times and between them; no font or desktop required.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProfileBubbleTimingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(60, 0.5)]
    [InlineData(120, 1)]
    [InlineData(300, 1)]
    [InlineData(450, 1)]
    [InlineData(637.5, 0.5625)]
    [InlineData(825, 0.25)]
    [InlineData(1012.5, 0.0625)]
    [InlineData(1200, 0)]
    public Task GoldRingRisesHoldsAndFadesWithWpfQuadraticEaseOut(double ms, double opacity) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var animation = MainShellWindow.CreateProfileBubbleGlowAnimation();
        Assert.Equal(TimeSpan.FromMilliseconds(1200), animation.Duration);
        var keys = animation.Children.ToArray();
        double Value(int i) => (double)Assert.Single(keys[i].Setters.Cast<global::Avalonia.Styling.Setter>(),
            s => s.Property == Visual.OpacityProperty).Value!;
        var t = ms / animation.Duration.TotalMilliseconds;
        int end = Array.FindIndex(keys, k => k.Cue.CueValue >= t);
        double actual;
        if (end == 0) actual = Value(0);
        else
        {
            double progress = (t - keys[end - 1].Cue.CueValue) / (keys[end].Cue.CueValue - keys[end - 1].Cue.CueValue);
            progress = keys[end].KeySpline?.GetSplineProgress(progress) ?? progress;
            actual = Value(end - 1) + (Value(end) - Value(end - 1)) * progress;
        }
        Assert.InRange(actual, 0, 1);
        Assert.Equal(opacity, actual, 4);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, -12)]
    [InlineData(0.105, -3)]
    [InlineData(0.245, -18.75)]
    [InlineData(0.385, -7.95)]
    [InlineData(0.525, -13.8)]
    [InlineData(0.6125, -12.527207793864215)]
    [InlineData(0.7, -12)]
    public void TierWobbleSettlesWithWpfSineEaseOut(double seconds, double angle) =>
        Assert.Equal(angle, MainShellWindow.ProfileTierWobbleAngle(seconds), 6);
}
