using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
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

/// <summary>From trunk TransformTweenTests (c18c2d8bb). Animation.RunAsync on a Transform object
/// throws (TransformAnimator casts to Visual), which is why the OG spin and the vault breath were
/// inert; they now run on BeatLoop.</summary>
public sealed class AnimationOnTransformTests
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
        // If Avalonia ever learns this, a Transform loop could go back to an Animation.
        Assert.ThrowsAny<InvalidCastException>(() => { _ = anim.RunAsync(new RotateTransform()); });
        return Task.CompletedTask;
    });
}
