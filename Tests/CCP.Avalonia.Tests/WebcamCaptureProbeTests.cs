using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
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
        using var source = new OpenCvFrameSource((_, _, mjpg) => { attempts.Add(mjpg); return mjpg ? next : first; }, 40, 1);
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
        using (var source = new OpenCvFrameSource((_, _, _) => moving, 40, 1))
            Assert.True(source.Open());
        Assert.True(moving.Reads >= 2);
        var flat = new List<Frames>();
        using var failed = new OpenCvFrameSource((_, _, _) => { var f = new Frames(); flat.Add(f); return f; }, 20, 1);
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
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => new Frames(), 20, 1);
            Assert.False(tracker.Start());
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.StartsWith("Camera unavailable", tracker.LastError);
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => new Frames { Opens = false }, 20, 1);
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
        using var source = new OpenCvFrameSource((_, _, _) => { attempts++; return frames; }, 100, 1);
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
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => { attempts++; return frames; }, 100, 1);
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

    private sealed class LateAttempt(bool blockRead) : IFrameSource
    {
        internal readonly ManualResetEventSlim Entered = new(false), Release = new(false), Closed = new(false);
        internal int Reads;
        public bool Open() { if (!blockRead) { Entered.Set(); Release.Wait(TimeSpan.FromSeconds(10)); } return true; }
        public bool Read(Mat frame) { Interlocked.Increment(ref Reads); if (blockRead) { Entered.Set(); Release.Wait(TimeSpan.FromSeconds(10)); } return false; }
        public void Dispose() => Closed.Set();
    }

    [Theory] // WPF WebcamTrackingService.cs:1188-1205,1304-1305,1418
    [InlineData(false)]
    [InlineData(true)]
    public void TimedOutAttemptCannotReadAgainOrRetryWhenDriverReturns(bool blockRead) => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout);
        var late = new LateAttempt(blockRead);
        int attempts = 0;
        try
        {
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            tracker.CameraOpenTimeout = TimeSpan.FromMilliseconds(100);
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => { attempts++; return late; }, 2000, 1);
            Assert.False(tracker.Start());
            Assert.True(late.Entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.False(tracker.StartWasStopped);
            Assert.False(late.Closed.IsSet);
            late.Release.Set();
            Assert.True(late.Closed.Wait(TimeSpan.FromSeconds(8)));
            Assert.True(SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(8)));
            Assert.Equal(1, attempts);
            Assert.Equal(blockRead ? 1 : 0, late.Reads);
        }
        finally
        {
            late.Release.Set();
            SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(10));
            tracker.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout) = old;
        }
    });

    [Fact] // WPF WebcamTrackingService.cs:1287-1294 and MainWindow.LabTab.cs:636-639
    public Task RetryReachesSplashInTheActiveLanguage() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var loc = LocalizationManager.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, loc.CurrentLanguage);
        var late = new LateAttempt(false);
        var shell = new MainShellWindow();
        var progress = new List<(double Value, string Text)>();
        Action<double, string> record = (value, text) => progress.Add((value, text));
        Task<bool>? start = null;
        try
        {
            loc.SetLanguage("de");
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            int attempt = 0;
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => ++attempt == 1 ? new Frames { Opens = false } : late, 20, 1);
            tracker.OnStartupProgress += record;
            shell.Show();
            start = tracker.StartAsync();
            var total = OperatingSystem.IsWindows() ? 4 : 2;
            var retry = Loc.GetF("webcam_capture_retry_fmt", 2, total);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!progress.Exists(x => x.Text == retry)) { Assert.True(DateTime.UtcNow < deadline); await Task.Delay(10); }
            while (!late.Entered.IsSet) { Assert.True(DateTime.UtcNow < deadline); await Task.Delay(10); }
            Assert.False(start.IsCompleted);
            Assert.Equal(0.55 + 0.37 / total, progress.Find(x => x.Text == retry).Value, 3);
            Assert.Equal(retry, shell.WebcamLoadingSplashForTests!.FindControl<TextBlock>("TxtStatus")!.Text);
            late.Release.Set();
            Assert.False(await start);
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.Equal("Kamera nicht verfügbar. Sie wird möglicherweise von einer anderen App verwendet oder durch die Kamera-Datenschutzeinstellungen blockiert.", tracker.LastError);
        }
        finally
        {
            late.Release.Set();
            if (start != null) await start;
            tracker.OnStartupProgress -= record;
            tracker.Stop(); shell.Close();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = (old.WebcamConsentGiven, old.WebcamConsentVersion, old.SourceFactory);
            loc.SetLanguage(old.CurrentLanguage);
        }
    });

    [Fact] // WPF MainWindow.LabTab.cs:636-639: the reported camera error reaches the splash
    public void InUseErrorUsesTheActiveLanguageAndAllLanguagesHaveBothMessages() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var loc = LocalizationManager.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, loc.CurrentLanguage);
        try
        {
            loc.SetLanguage("de"); s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => new Frames(), 20, 1);
            Assert.False(tracker.Start());
            Assert.Equal(WebcamTrackingState.CameraInUse, tracker.State);
            Assert.Equal("Kamera nicht verfügbar. Sie wird möglicherweise von einer anderen App verwendet oder durch die Kamera-Datenschutzeinstellungen blockiert.", tracker.LastError);
            loc.SetLanguage("en");
            var englishError = Loc.Get("webcam_camera_unavailable_detail");
            var englishRetry = Loc.Get("webcam_capture_retry_fmt");
            foreach (var language in LocalizationManager.AvailableLanguages)
            {
                loc.SetLanguage(language.Code);
                var error = Loc.Get("webcam_camera_unavailable_detail");
                var hint = Loc.GetF("webcam_capture_retry_fmt", 2, 4);
                Assert.DoesNotContain("{0}", hint); Assert.DoesNotContain("{1}", hint);
                Assert.Contains("2", hint); Assert.Contains("4", hint);
                if (language.Code != "en") { Assert.NotEqual(englishError, error); Assert.NotEqual(englishRetry, Loc.Get("webcam_capture_retry_fmt")); }
            }
        }
        finally
        {
            tracker.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory) = (old.WebcamConsentGiven, old.WebcamConsentVersion, old.SourceFactory);
            loc.SetLanguage(old.CurrentLanguage);
        }
    });

    [Fact] // WPF WebcamTrackingService.cs:1193-1205: a timed-out attempt must not update the splash later
    public void QueuedRetryIsDroppedAfterTimeout() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var tracker = WebcamTracker.Instance;
        var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout);
        var late = new LateAttempt(false);
        var progress = new List<string>();
        Action<double, string> record = (_, text) => progress.Add(text);
        try
        {
            s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            tracker.CameraOpenTimeout = TimeSpan.FromMilliseconds(100);
            int attempt = 0;
            WebcamTracker.SourceFactory = () => new OpenCvFrameSource((_, _, _) => ++attempt == 1 ? new Frames { Opens = false } : late, 20, 1);
            tracker.OnStartupProgress += record;
            Assert.False(tracker.Start());
            Assert.True(late.Entered.Wait(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(Loc.GetF("webcam_capture_retry_fmt", 2, OperatingSystem.IsWindows() ? 4 : 2), progress);
        }
        finally
        {
            late.Release.Set();
            SpinWait.SpinUntil(() => !tracker.IsClosing, TimeSpan.FromSeconds(10));
            tracker.OnStartupProgress -= record; tracker.Stop();
            (s.WebcamConsentGiven, s.WebcamConsentVersion, WebcamTracker.SourceFactory, tracker.CameraOpenTimeout) = old;
        }
    });

    [Fact] // WPF WebcamTrackingService.cs:1236-1237,1296: one device per open, even across retries
    public void DeviceChangeDuringRetryAppliesOnlyOnNextStart()
    {
        var s = CoreSettings.Current; var old = (s.WebcamDeviceIndex, V4l2Cameras.Root, Environment.GetEnvironmentVariable("CCP_WEBCAM_DEVICE"));
        var indices = new List<int>();
        try
        {
            Environment.SetEnvironmentVariable("CCP_WEBCAM_DEVICE", null); V4l2Cameras.Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString()); s.WebcamDeviceIndex = 1;
            using var first = new OpenCvFrameSource((index, _, _) => { indices.Add(index); return new Frames { Paint = (_, _) => s.WebcamDeviceIndex = 2 }; }, 20, 1);
            Assert.False(first.Open()); Assert.Equal(2, s.WebcamDeviceIndex); Assert.True(indices.Count >= 2); Assert.All(indices, i => Assert.Equal(1, i));
            indices.Clear();
            using var second = new OpenCvFrameSource((index, _, _) => { indices.Add(index); return new Frames { Opens = false }; }, 20, 1);
            Assert.False(second.Open()); Assert.True(indices.Count >= 2); Assert.All(indices, i => Assert.Equal(2, i));
        }
        finally { (s.WebcamDeviceIndex, V4l2Cameras.Root) = (old.WebcamDeviceIndex, old.Root); Environment.SetEnvironmentVariable("CCP_WEBCAM_DEVICE", old.Item3); }
    }

}
