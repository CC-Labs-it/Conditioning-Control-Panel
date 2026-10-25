using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using OpenCvSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class WebcamGestureTests
{
    // Synthetic lips: a 20x(20*MAR) rectangle. All remaining lip-ring points lie inside it.
    private static float[][] Lips(float mar)
    {
        var lm = Enumerable.Range(0, 468).Select(_ => new[] { 20f, 20f }).ToArray();
        lm[78] = new[] { 10f, 20f }; lm[308] = new[] { 30f, 20f };
        foreach (int i in new[] { 81, 13, 311 }) lm[i] = new[] { 20f, 20 - 10 * mar };
        foreach (int i in new[] { 178, 14, 402 }) lm[i] = new[] { 20f, 20 + 10 * mar };
        return lm;
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.6)]
    public void MarUsesThreeVerticalChordsNormalizedByWidth(double mar)
        => Assert.Equal(mar, MouthOpenDetector.ComputeMar(Lips((float)mar)), 5);

    [Fact]
    public void DegenerateMouthIsNotOpen()
    {
        var lm = Lips(0.6f); lm[308] = lm[78];
        Assert.Equal(0, MouthOpenDetector.ComputeMar(lm));
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, Scalar.All(0));
        foreach (var p in lm) { p[0] = -100; p[1] = -100; }
        Assert.Equal(0, WebcamTracker.TongueRatio(bgr, lm));
    }

    [Theory]
    [InlineData(0, 0, 200, 1)]    // red/pink
    [InlineData(200, 0, 0, 0)]    // blue counts as other
    [InlineData(255, 255, 255, 0)] // teeth excluded
    [InlineData(0, 0, 0, 0)]      // shadow excluded
    public void TongueClassifiesOnlyUsefulPixelsInsideLipPolygon(int b, int g, int r, double expected)
    {
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, new Scalar(b, g, r));
        Assert.Equal(expected, WebcamTracker.TongueRatio(bgr, Lips(0.6f)));
    }

    [Fact]
    public void PinkPixelsOutsideTheLipPolygonDoNotCount()
    {
        int[] lip = { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };
        var lm = Lips(0.6f);
        for (int i = 0; i < lip.Length; i++)
        {
            double angle = i * 2 * Math.PI / lip.Length;
            lm[lip[i]] = new[] { (float)(20 + 10 * Math.Cos(angle)), (float)(20 + 8 * Math.Sin(angle)) };
        }
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, new Scalar(0, 0, 200));
        // Blue covers the complete lip polygon; pink remains in its bounding-box corners.
        Cv2.Ellipse(bgr, new OpenCvSharp.Point(20, 20), new OpenCvSharp.Size(11, 9), 0, 0, 360,
            new Scalar(200, 0, 0), -1);
        Assert.Equal(0, WebcamTracker.TongueRatio(bgr, lm));
    }

    /// <summary>Counts detector fires, not events: an idle tracker never delivers (pinned by
    /// WebcamCalibrationTests.Tracker_UsesCoreMarAndDropsStoppedRunEvents).</summary>
    [Fact]
    public void RealPixelPathFiresMouthThenTongueOnceAndFaceLossCancelsHold() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        int mouth = 0, tongue = 0;
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, new Scalar(0, 0, 200));
        var now = new DateTime(2026, 1, 1);
        var sensitivity = CoreSettings.Current.WebcamSensitivity;
        void Frame(float mar, DateTime t)
        {
            if (tracker.ProcessMouthLandmarks(Lips(mar), t)) mouth++;
            if (tracker.ProcessTongue(bgr, Lips(mar), t)) tongue++;
        }
        try
        {
            CoreSettings.Current.WebcamSensitivity = 0.5;
            for (int i = 0; i < 90; i++) Frame(0.1f, now.AddMilliseconds(i * 33));
            for (int i = 90; i < 150; i++) Frame(0.6f, now.AddMilliseconds(i * 33));
            Assert.Equal(1, mouth); Assert.Equal(1, tongue);
            tracker.NoFace();
            Frame(0.6f, now.AddSeconds(10));
            Assert.Equal(1, mouth); Assert.Equal(1, tongue);
        }
        finally { CoreSettings.Current.WebcamSensitivity = sensitivity; }
    });

    [Fact]
    public void LongStareFollowsPublishedGazeAndRespectsCooldownAndFaceLoss() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        int moves = 0, stares = 0;
        tracker.OnGazeMove += _ => moves++;
        tracker.OnLongStare += p => { Assert.Equal(new global::Avalonia.Point(200, 100), p); stares++; };
        var now = new DateTime(2026, 1, 1);
        for (int i = 0; i <= 30; i++) tracker.EmitProjectedGaze(200, 100, now.AddMilliseconds(i * 110));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(31, moves); Assert.Equal(1, stares);
        for (int i = 0; i < 100; i++) tracker.NoFace();
        for (int i = 0; i < 4; i++) tracker.EmitProjectedGaze(200, 100, now.AddSeconds(9).AddMilliseconds(i * 110));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, stares); // old samples cannot complete a stare on reacquisition
    });

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void DevicesDiagnosticsSubscribeOnceAndDetachWhenHidden() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var section = new DevicesSettingsSection();
        var owner = new global::Avalonia.Controls.Window { Content = section };
        owner.Show();
        var tracker = WebcamTracker.Instance;
        var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
        try
        {
            tracker.RaiseBlinkForTest();
            Assert.Contains("Blink #1", log.Text);
            owner.Content = null;
            tracker.RaiseBlinkForTest();
            Assert.DoesNotContain("Blink #2", log.Text);
            owner.Content = section;
            tracker.RaiseBlinkForTest();
            Assert.Contains("Blink #2", log.Text);
            Assert.DoesNotContain("Blink #3", log.Text);
        }
        finally { owner.Close(); }
    });

    /// <summary>The status pill carries {loc:Str rf_webcam_stopped}; the live state is a bound key, so a
    /// language change shows the current state in the new language, not a frozen or reverted text.</summary>
    [Fact]
    public void DevicesStatusFollowsTheLanguageWhileTracking() => WebcamCalibrationTests.WithTracker(start: false, (tracker, _) =>
    {
        // Parked in Read: blank frames would race the assertion into "Face lost".
        var source = new ParkedSource();
        WebcamTracker.SourceFactory = () => source;
        Assert.True(tracker.Start(), tracker.LastError);
        var section = new DevicesSettingsSection();
        var owner = new global::Avalonia.Controls.Window { Content = section };
        owner.Show();
        var status = section.FindControl<TextBlock>("TxtWebcamDebugStatus")!;
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(tracker.IsRunning);
            Assert.Equal(Loc.Get("rf_webcam_tracking"), status.Text);
            LocalizationManager.Instance.SetLanguage("ja");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("追跡中", status.Text);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); owner.Close(); source.Release.Set(); }
    });

    private sealed class ParkedSource : IFrameSource
    {
        public readonly ManualResetEventSlim Release = new();
        public bool Open() => true;
        public bool Read(Mat bgr) { Release.Wait(); return false; }
        public void Dispose() { }
    }

    /// <summary>WPF :1505: a panic while the camera is still opening abandons the open; the failed
    /// start reads as stopped, not as "no camera".</summary>
    [Fact]
    public void PanicDuringCameraOpenReadsAsStoppedNotNoCamera() => WebcamCalibrationTests.WithTracker(start: false, (tracker, _) =>
    {
        WebcamTracker.SourceFactory = () => new PanicOnOpen(tracker);
        Assert.False(tracker.Start());
        Assert.True(tracker.StartWasStopped);
        Assert.Equal("Webcam tracking was stopped before the camera finished opening.", tracker.LastError);
    });

    private sealed class PanicOnOpen(WebcamTracker tracker) : IFrameSource
    {
        public bool Open() { tracker.Stop(); return false; }   // panic mid-open bumps the generation
        public bool Read(Mat bgr) => false;
        public void Dispose() { }
    }
}
