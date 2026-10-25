using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebcamOpenSafetyTests
{
    private sealed class Source : IFrameSource
    {
        internal readonly ManualResetEventSlim Entered = new(false), Release = new(false), Disposed = new(false);
        public bool Open() { Entered.Set(); Release.Wait(); return true; }
        public bool Read(Mat frame) => false;
        public void Dispose() => Disposed.Set();
    }

    [Fact]
    public void TimeoutDoesNotDisposeUnderTheDriverAndLateSuccessIsReleased()
    {
        var source = new Source();
        var attempt = new CameraOpenAttempt();
        try
        {
            Assert.Null(attempt.Open(() => source, () => false, TimeSpan.FromMilliseconds(100)));
            Assert.True(source.Entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(attempt.IsPending);
            Assert.False(source.Disposed.IsSet);
        }
        finally { source.Release.Set(); }
        Assert.True(source.Disposed.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !attempt.IsPending, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PanicDuringOpenDropsTheResultAndSuccessfulOpenTransfersOwnership()
    {
        var source = new Source();
        int stopped = 0;
        var attempt = new CameraOpenAttempt();
        var opening = Task.Run(() => attempt.Open(() => source, () => Volatile.Read(ref stopped) != 0, TimeSpan.FromSeconds(10)));
        try
        {
            Assert.True(source.Entered.Wait(TimeSpan.FromSeconds(5)));
            Volatile.Write(ref stopped, 1);
        }
        finally { source.Release.Set(); }
        Assert.Null(await opening);
        Assert.True(source.Disposed.IsSet);

        var good = new Source();
        good.Release.Set();
        Assert.Same(good, new CameraOpenAttempt().Open(() => good, () => false, TimeSpan.FromSeconds(5)));
        Assert.False(good.Disposed.IsSet);
        good.Dispose();
    }

    [Fact]
    public void ProbeRejectsFlatStaticButAcceptsSpatialContrastOrDimMotion()
    {
        using var previous = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(0));
        using var probe = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(0));
        using var diff = new Mat();
        Assert.False(OpenCvFrameSource.ProbeUsable(probe, previous, diff));
        probe.SetTo(Scalar.All(1)); // noise alone is insufficient
        Assert.False(OpenCvFrameSource.ProbeUsable(probe, previous, diff));
        probe.SetTo(Scalar.All(2)); // exact WPF motion threshold
        Assert.True(OpenCvFrameSource.ProbeUsable(probe, previous, diff));
        probe.SetTo(Scalar.All(0));
        Cv2.Rectangle(probe, new Rect(0, 0, 10, 20), Scalar.All(20), -1);
        using var empty = new Mat();
        Assert.True(OpenCvFrameSource.ProbeUsable(probe, empty, diff));
        probe.SetTo(Scalar.All(0));
        Assert.False(OpenCvFrameSource.ProbeUsable(probe, empty, diff));
    }
}
