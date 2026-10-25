using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The real projected-gaze stage over synthetic points: no camera or models.</summary>
public sealed class WebcamProjectionTests
{
    [Fact]
    public void TargetLockContractsBeforeFollower_AndPreLockSnapshotIsUnflattered() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        var emitted = new List<Point>();
        tracker.OnGazeMove += emitted.Add;
        tracker.SetGazeAttractor(100, 100, 50);
        tracker.EmitProjectedGaze(120, 100, DateTime.UtcNow);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Point(120, 100), tracker.LastPreLockGaze);
        Assert.Equal(120 - 20 * 0.93 * 0.22, emitted[0].X, 8); // first follower frame is exact
        for (int i = 0; i < 80; i++) tracker.EmitProjectedGaze(120, 100, DateTime.UtcNow);
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(emitted[^1].X, 101.39, 101.41);
        tracker.ClearGazeAttractor();
        for (int i = 0; i < 100; i++) tracker.EmitProjectedGaze(120, 100, DateTime.UtcNow);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(120, emitted[^1].X, 5);
    });

    [Fact]
    public void LongStareUsesEmittedPoints_CoolsDown_AndFaceLossDropsPendingHistory() => AvaloniaTestDispatcher.Run(() =>
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new WebcamTracker();
        var hits = new List<Point>();
        tracker.OnLongStare += hits.Add;
        for (int i = 0; i < 30; i++) tracker.EmitProjectedGaze(500, 300, start.AddMilliseconds(i * 110));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Point(500, 300), Assert.Single(hits));
        tracker.EmitProjectedGaze(500, 300, start.AddSeconds(4));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(hits);

        var fresh = new WebcamTracker();
        fresh.OnLongStare += hits.Add;
        for (int i = 0; i < 28; i++) fresh.EmitProjectedGaze(500, 300, start.AddMilliseconds(i * 110));
        for (int i = 0; i <= GazeEngine.FaceLostFramesThreshold * 2; i++) fresh.NoFace();
        fresh.EmitProjectedGaze(500, 300, start.AddMilliseconds(3080));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(hits); // without face-loss clear this frame would complete the stare
    });
}
