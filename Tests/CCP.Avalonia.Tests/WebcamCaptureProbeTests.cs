using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamCaptureProbeTests
{
    private sealed class Frames : IFrameSource
    {
        internal bool Opens = true, Disposed;
        internal int Reads;
        internal Action<Mat, int>? Paint;
        public bool Open() => Opens;
        public bool Read(Mat frame)
        {
            frame.Create(20, 20, MatType.CV_8UC3);
            frame.SetTo(Scalar.All(10));
            Reads++;
            Paint?.Invoke(frame, Reads);
            return true;
        }
        public void Dispose() => Disposed = true;
    }

    [Fact] // WPF WebcamTrackingService.cs:1269-1305,1371-1382,1414-1458
    public void FlatDefaultIsDisposedAndMjpgIsAdoptedWhenItWarmsUp()
    {
        var first = new Frames();
        var next = new Frames { Paint = (frame, read) =>
        {
            if (read >= 3) Cv2.Rectangle(frame, new Rect(0, 0, 10, 20), Scalar.All(100), -1);
        }};
        var attempts = new List<bool>();
        using var source = new OpenCvFrameSource((_, mjpg) => { attempts.Add(mjpg); return mjpg ? next : first; }, 40, 1);
        Assert.True(source.Open());
        Assert.Equal(new[] { false, true }, attempts);
        Assert.True(first.Disposed);
        Assert.False(next.Disposed);
        Assert.True(next.Reads >= 3);
        Assert.True(source.AnyOpened);
        using var frame = new Mat();
        Assert.True(source.Read(frame));
        source.Dispose();
        Assert.True(next.Disposed);
    }

    [Fact] // WPF :1422-1456: a dim feed can pass through temporal change, a static flat feed cannot
    public void TemporalChangeAcceptsADimFeedAndAStaticFeedFails()
    {
        var moving = new Frames { Paint = (frame, read) => frame.SetTo(Scalar.All(read == 1 ? 10 : 12)) };
        using (var source = new OpenCvFrameSource((_, _) => moving, 40, 1))
            Assert.True(source.Open());
        Assert.True(moving.Reads >= 2);
        var flat = new List<Frames>();
        using var failed = new OpenCvFrameSource((_, _) => { var f = new Frames(); flat.Add(f); return f; }, 20, 1);
        Assert.False(failed.Open());
        Assert.True(failed.AnyOpened);
        Assert.All(flat, f => Assert.True(f.Disposed));
    }

    [Fact] // WPF :1318-1333 and MainWindow.LabTab.cs:636-643
    public void RealStartReportsUnusableOpenAsInUseAndFailedOpensAsDenied() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        try
        {
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _) => new Frames(), 20, 1);
            Assert.False(tracker.Start());
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.StartsWith("Camera unavailable", tracker.LastError);
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _) => new Frames { Opens = false }, 20, 1);
            Assert.False(tracker.Start());
            Assert.Equal(WebcamTrackingState.CameraDenied, tracker.State);
        }
        finally
        {
            tracker.Stop();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    [Fact] // WPF :1304-1305,1418: stop between reads never opens another attempt
    public void CancelledProbeDisposesItsHandleAndDoesNotRetry()
    {
        var frames = new Frames();
        int attempts = 0;
        using var source = new OpenCvFrameSource((_, _) => { attempts++; return frames; }, 100, 1);
        source.Cancelled = () => frames.Reads > 0;
        Assert.False(source.Open());
        Assert.Equal(1, attempts);
        Assert.True(frames.Disposed);
    }
    [Fact] // WPF :1304-1305,1418 and LabTab.cs:648-649: stopping startup closes the splash
    public void StopDuringTheProbeLeavesTrackerStopped() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory);
        var tracker = WebcamTracker.Instance;
        var frames = new Frames { Paint = (_, _) => tracker.Stop() };
        int attempts = 0;
        try
        {
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _) => { attempts++; return frames; }, 100, 1);
            Assert.False(tracker.Start());
            Assert.True(tracker.StartWasStopped);
            Assert.Equal(WebcamTrackingState.Stopped, tracker.State);
            Assert.Equal(1, attempts);
            Assert.True(frames.Disposed);
        }
        finally
        {
            tracker.Stop();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory) = old;
        }
    });

    private sealed class WedgedOpen : IFrameSource
    {
        internal readonly ManualResetEventSlim Entered = new(false), Release = new(false), Closed = new(false);
        internal long EnteredAt;
        public bool Open() { EnteredAt = Stopwatch.GetTimestamp(); Entered.Set(); Release.Wait(TimeSpan.FromSeconds(2)); return false; }
        public bool Read(Mat frame) => false;
        public void Dispose() => Closed.Set();
    }

    [Fact] // WPF WebcamTrackingService.cs:1164-1205: timeout never disposes a handle inside native code
    public void OpenWatchdogReportsInUseAndLateWorkerOwnsCleanup() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout);
        var source = new WedgedOpen();
        int attempts = 0;
        try
        {
            settings.WebcamConsentGiven = true;
            settings.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            tracker.CameraOpenTimeout = TimeSpan.FromMilliseconds(100);
            WebcamTracker.SourceFactory = () => { attempts++; return source; };
            Assert.False(tracker.Start());
            Assert.True(source.Entered.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(Stopwatch.GetElapsedTime(source.EnteredAt) < TimeSpan.FromSeconds(1));
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.False(source.Closed.IsSet);
            Assert.True(tracker.IsClosing);
            Assert.False(tracker.Start());
            Assert.Equal(1, attempts);
            source.Release.Set();
            Assert.True(source.Closed.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            source.Release.Set();
            SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(5));
            tracker.Stop();
            (settings.WebcamConsentGiven, settings.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout) = old;
        }
    });

}
