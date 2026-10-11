using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>A test clock stepped by hand (P08).</summary>
internal sealed class SteppedTime : TimeProvider
{
    private long _now = TimeSpan.FromSeconds(10).Ticks;
    public override long GetTimestamp() => _now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(TimeSpan by) => _now += by.Ticks;
}

/// <summary>A frame source stepped by the test.</summary>
internal sealed class FakeFrames : IFrameSource
{
    private readonly List<Action<TimeSpan>> _subs = new();
    public int Count => _subs.Count;
    public void Subscribe(Action<TimeSpan> onFrame) { if (!_subs.Contains(onFrame)) _subs.Add(onFrame); }
    public void Unsubscribe(Action<TimeSpan> onFrame) => _subs.Remove(onFrame);
    public void Frame(TimeSpan t) { foreach (var s in _subs.ToArray()) s(t); }

    /// <summary>Deliver <paramref name="seconds"/> of frames at <paramref name="hz"/>, starting at <paramref name="startMs"/>.</summary>
    public void Run(double hz, double seconds, double startMs = 1000)
    {
        int n = (int)Math.Round(hz * seconds);
        for (int i = 0; i < n; i++) Frame(TimeSpan.FromMilliseconds(startMs + i * 1000.0 / hz));
    }
}

/// <summary>IMPORTED from trunk (c18c2d8bb). Animation.RunAsync on a Transform object throws
/// (TransformAnimator casts to Visual); TransformTween is the road.</summary>
public sealed class TransformTweenTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task AnimationRunAsyncOnATransformStillThrows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var anim = new Animation { Duration = TimeSpan.FromMilliseconds(100) };
        anim.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 10.0) } });
        // If Avalonia ever learns this, TransformTween can retire.
        Assert.ThrowsAny<InvalidCastException>(() => { _ = anim.RunAsync(new RotateTransform()); });
        return Task.CompletedTask;
    });

    [Fact]
    public Task TweenWritesTheKeysAndEndsOnTheLast() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var time = new SteppedTime();
        TransformTween.Time = time;
        try
        {
            var turn = new RotateTransform { Angle = 99 };
            var tween = TransformTween.Run(turn, TimeSpan.FromMilliseconds(120),
                new (double, AvaloniaProperty, double)[] { (0, RotateTransform.AngleProperty, 0.0), (0.5, RotateTransform.AngleProperty, -17.0), (1, RotateTransform.AngleProperty, 5.0) });
            Assert.Equal(0, turn.Angle, 3);                    // the first key lands at once
            time.Advance(TimeSpan.FromMilliseconds(30));
            tween.Step();
            Assert.Equal(-8.5, turn.Angle, 3);                 // halfway to the middle key
            time.Advance(TimeSpan.FromMilliseconds(60));
            tween.Step();
            Assert.Equal(-6.0, turn.Angle, 3);                 // halfway back up
            Assert.True(tween.IsRunning);
            time.Advance(TimeSpan.FromMilliseconds(100));
            tween.Step();
            Assert.False(tween.IsRunning);
            Assert.Equal(5.0, turn.Angle, 3);
        }
        finally { TransformTween.Time = TimeProvider.System; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BreathFollowsTheSineCurveOnTheBeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var time = new SteppedTime();
        BreathClock.Time = time;
        try
        {
            var frames = new FakeFrames();
            var breath = new BreathClock(frames, periodSeconds: 2);
            var tile = new Border();
            breath.Start((tile, 0.2, 0.8));
            Assert.True(breath.IsRunning);
            Assert.Equal(0.2, tile.Opacity, 3);                // starts at the low end
            time.Advance(TimeSpan.FromSeconds(1));
            frames.Frame(TimeSpan.FromMilliseconds(100));
            Assert.Equal(0.5, tile.Opacity, 3);                // half a period: midway
            time.Advance(TimeSpan.FromSeconds(1));
            frames.Frame(TimeSpan.FromMilliseconds(105));      // 5 ms later: not a beat, no write
            Assert.Equal(0.5, tile.Opacity, 3);
            frames.Frame(TimeSpan.FromMilliseconds(140));
            Assert.Equal(0.8, tile.Opacity, 3);                // a full period: the top
            breath.Stop();
            Assert.False(breath.IsRunning);
            Assert.Equal(0, frames.Count);
        }
        finally { BreathClock.Time = TimeProvider.System; }
        return Task.CompletedTask;
    });
}
