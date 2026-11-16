using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalHostedFrameTests
{
    private sealed class Clock
    {
        internal readonly List<(double At, Action Run)> Timers = new();
        internal double Now;
        internal void Schedule(Action action, TimeSpan delay) => Timers.Add((Now + delay.TotalMilliseconds, action));
        internal void Advance(double target)
        {
            while (Timers.Where(t => t.At <= target).OrderBy(t => t.At).FirstOrDefault() is { Run: not null } due)
            {
                Timers.Remove(due); Now = due.At; due.Run();
            }
            Now = target;
        }
    }

    // WPF SubliminalService.cs:842-873: the hold follows the complete fade-in, which starts once the stage draws.
    [Fact]
    public void Delayed_first_frame_keeps_the_full_fade_and_hold() => SubliminalCompositorRouteTests.With(env =>
    {
        var clock = new Clock();
        Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(100), clock.Schedule));
        var stage = Assert.Single(env.Stages);
        var canvas = Assert.IsType<Grid>(stage.Content);
        var card = Assert.Single(canvas.Children);
        stage.Measure(new Size(300, 200));
        stage.Arrange(new Rect(0, 0, 300, 200));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        clock.Advance(500); // later than the whole envelope would have run from Show
        Assert.Same(card, Assert.Single(canvas.Children));
        Assert.Equal(0, card.Opacity);
        Assert.Empty(SubliminalOverlay.ActiveTextRects());
        var frame = Assert.Single(env.Frames);
        frame(TimeSpan.Zero);
        var count = clock.Timers.Count;
        frame(TimeSpan.Zero); // a duplicate frame cannot restart the envelope
        Assert.Equal(count, clock.Timers.Count);
        clock.Advance(550);
        Assert.Equal(0.8, card.Opacity, 3);
        Assert.Single(SubliminalOverlay.ActiveTextRects()); // hosted text is still excluded from OCR
        clock.Advance(650);
        Assert.Equal(0.8, card.Opacity, 3);
        clock.Advance(675);
        Assert.InRange(card.Opacity, 0.01, 0.79);
        clock.Advance(700);
        Assert.Empty(canvas.Children);
        Assert.False(stage.IsVisible); // one-shot stage released
    });

    [Theory]
    [InlineData("timeout")]
    [InlineData("stop-before-frame")]
    [InlineData("stop-after-frame")]
    public void Timeout_and_stop_retire_the_card_and_its_stage(string reason) => SubliminalCompositorRouteTests.With(env =>
    {
        var clock = new Clock();
        Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(100), clock.Schedule));
        var stage = Assert.Single(env.Stages);
        var canvas = Assert.IsType<Grid>(stage.Content);
        var card = Assert.Single(canvas.Children);
        var frame = Assert.Single(env.Frames);
        if (reason == "stop-after-frame") frame(TimeSpan.Zero);
        if (reason == "timeout")
        {
            clock.Advance(2999);
            Assert.Same(card, Assert.Single(canvas.Children));
            clock.Advance(3000);
        }
        else SubliminalOverlay.CloseAll();
        Assert.Empty(canvas.Children);
        Assert.False(stage.IsVisible);
        var count = clock.Timers.Count;
        frame(TimeSpan.Zero);
        Assert.Equal(count, clock.Timers.Count);
        clock.Advance(6000);
        Assert.Equal(0, card.Opacity);
        Assert.Empty(canvas.Children);
    });
}
