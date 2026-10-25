using System;
using System.Reflection;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamTongueTests
{
    [Fact]
    public void WindowsEnumerationNeverTouchesComOnOtherPlatforms()
    {
        if (OperatingSystem.IsWindows()) return; // Windows COM requires real hardware; compiled here only.
        Assert.Empty(WindowsCameras.Enumerate());
        Assert.Empty(WindowsCameras.EnumerateStrict());
    }

    private static float[][] Lips()
    {
        var lm = new float[468][];
        for (int i = 0; i < lm.Length; i++) lm[i] = new float[] { 5, 5, 0 };
        int[] indices = { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };
        for (int i = 0; i < indices.Length; i++)
        {
            double angle = 2 * Math.PI * i / indices.Length;
            lm[indices[i]] = new[] { (float)(5 + 4 * Math.Cos(angle)), (float)(5 + 4 * Math.Sin(angle)), 0 };
        }
        return lm;
    }

    [Fact]
    public void PolygonCountsOnlyInnerLipPixels_ExcludesTeethAndShadow_AndRejectsDegenerateRegion()
    {
        var lips = Lips();
        using var frame = new Mat(11, 11, MatType.CV_8UC3, new Scalar(0, 150, 0));
        frame.Set(0, 0, new Vec3b(0, 0, 150)); // outside the lip polygon
        Assert.Equal(0, TongueOutDetector.ComputeRatio(frame, lips, out int outside, out _, out _, out _));
        Assert.Equal(0, outside);
        frame.SetTo(new Scalar(0, 0, 150));
        Assert.Equal(1, TongueOutDetector.ComputeRatio(frame, lips, out int tongue, out _, out _, out int other));
        Assert.True(tongue > 8); Assert.Equal(0, other);
        frame.SetTo(Scalar.All(255));
        Assert.Equal(0, TongueOutDetector.ComputeRatio(frame, lips, out _, out int teeth, out _, out _));
        Assert.True(teeth > 8);
        frame.Set(5, 5, new Vec3b(0, 0, 150));
        Assert.Equal(0, TongueOutDetector.ComputeRatio(frame, lips, out int sparse, out _, out _, out _));
        Assert.Equal(1, sparse); // fewer than eight useful pixels cannot fire
        frame.SetTo(Scalar.All(0));
        Assert.Equal(0, TongueOutDetector.ComputeRatio(frame, lips, out _, out _, out int shadow, out _));
        Assert.True(shadow > 8);
        foreach (var point in lips) { point[0] = -20; point[1] = -20; }
        Assert.Equal(0, TongueOutDetector.ComputeRatio(frame, lips, out _, out _, out _, out _));
    }

    [Fact]
    public void CapturePublishesTongueOnce_AndStopDropsQueuedDelivery() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        var runField = typeof(WebcamTracker).GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var run = Activator.CreateInstance(runField.FieldType, nonPublic: true)!;
        runField.SetValue(tracker, run); // synthetic session: no camera, models or thread
        var mouth = (MouthOpenDetector)typeof(WebcamTracker).GetField("_mouth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 20; i++) mouth.Update(.1, epoch.AddMilliseconds(i * 30));
        for (int i = 0; i < 20; i++) mouth.Update(.5, epoch.AddMilliseconds(1000 + i * 30));
        Assert.True(mouth.IsOpen);
        int events = 0;
        tracker.OnTongueOut += () => events++;
        using var frame = new Mat(11, 11, MatType.CV_8UC3, new Scalar(0, 0, 150));
        var lips = Lips();
        Assert.False(tracker.ProcessTongue(frame, lips, epoch.AddSeconds(2)));
        Assert.True(tracker.ProcessTongue(frame, lips, epoch.AddMilliseconds(2350)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, events);
        Assert.False(tracker.ProcessTongue(frame, lips, epoch.AddSeconds(4)));
        tracker.NoFace();
        Assert.False(tracker.ProcessTongue(frame, lips, epoch.AddSeconds(5))); // mouth reset
        for (int i = 0; i < 20; i++) mouth.Update(.5, epoch.AddMilliseconds(5100 + i * 30));
        Assert.False(tracker.ProcessTongue(frame, lips, epoch.AddSeconds(6)));
        Assert.True(tracker.ProcessTongue(frame, lips, epoch.AddMilliseconds(6350)));
        var generation = typeof(WebcamTracker).GetField("_gen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        generation.SetValue(tracker, 1); // StopAsync bumps this before its worker can clear _run
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, events);
        runField.SetValue(tracker, null);
    });
}
