using System;
using System.Reflection;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GazeAttractorTests
{
    private static GazeLock Lock => (GazeLock)typeof(WebcamTracker)
        .GetField("_gazeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(WebcamTracker.Instance)!;

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // WPF GazeFocusService.cs:506-546 and :290: acquisition and every release path.
    [Theory]
    [InlineData("face")]
    [InlineData("miss")]
    [InlineData("cooldown")]
    [InlineData("stop")]
    public void LiveBubbleDwellLocksAndReleases(string release) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        Lock.Clear(); Lock.Apply(0, 0);
        var previousCalibration = WebcamTracker.Instance.Calibration;
        WebcamTracker.Instance.Calibration = null;
        CoreSettings.Current.FlashGazePopEnabled = false;
        CoreSettings.Current.FlashGazeLingerEnabled = false;
        CoreSettings.Current.VideoGazeClickEnabled = false;
        CoreSettings.Current.RestrictGazeContentToCalibratedScreen = false;
        var clock = new Clock();
        var gaze = new GazeFocus { Clock = clock, CanRun = () => true, PopBubble = _ => { } };
        var bubble = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(bubble);
        try
        {
            gaze.MasterEnabled = true;
            gaze.HandleGazeMove(new Point(150, 150));
            gaze.Tick();
            Assert.InRange(Lock.Apply(170, 150).X, 150, 169.99);
            switch (release)
            {
                case "face": gaze.HandleFaceLost(); break;
                case "miss": bubble.Popping = true; break;
                case "cooldown": gaze.HandleBlink(); break;
                case "stop": gaze.Stop(); break;
            }
            if (release != "stop") gaze.Tick();
            Assert.Equal((170d, 150d), Lock.Apply(170, 150));
        }
        finally { gaze.Stop(); Lock.Clear(); Lock.Apply(0, 0); WebcamTracker.Instance.Calibration = previousCalibration; BubbleOverlay.Field.Bubbles.Remove(bubble); }
    });

    // WPF SetCursorLock :574-592: calibration ownership, inverse monitor translation and radius cap.
    [Theory]
    [InlineData(10, 50)]
    [InlineData(100, 80)]
    [InlineData(1000, 240)]
    public void LockUsesMonitorLocalDipsAndBoundedRadius(double size, double radius) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        Lock.Clear(); Lock.Apply(0, 0);
        var tracker = WebcamTracker.Instance;
        var previous = tracker.Calibration;
        try
        {
            tracker.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord
                { DeviceName = "left", X = -1920, Y = 300, DpiScale = 1.5 } };
            var center = GazeFocus.GazeToDesktop(new Point(150, 150));
            GazeFocus.SetCursorLock(new Rect(center.X - size / 2, center.Y - size / 2, size, size));
            var actual = Lock.Apply(150 + radius, 150);
            Assert.Equal(150 + radius * (1 - .93 * .22), actual.X, 6);
            Assert.Equal(150, actual.Y, 6);
            GazeFocus.SetCursorLock(new Rect(0, 0, 0, 0));
            Assert.Equal((170d, 150d), Lock.Apply(170, 150));
        }
        finally { tracker.Calibration = previous; Lock.Clear(); Lock.Apply(0, 0); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlashAndVideoDwellAlsoPullTowardTheirBounds(bool video) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        Lock.Clear(); Lock.Apply(0, 0);
        var previousCalibration = WebcamTracker.Instance.Calibration;
        WebcamTracker.Instance.Calibration = null;
        var settings = CoreSettings.Current;
        settings.RestrictGazeContentToCalibratedScreen = false;
        settings.BubbleGazePopEnabled = false;
        settings.FlashGazePopEnabled = !video;
        settings.FlashGazeLingerEnabled = false;
        settings.VideoGazeClickEnabled = video;
        var gaze = new GazeFocus { CanRun = () => true };
        var flash = new FlashOverlayWindow();
        var canvas = new global::Avalonia.Controls.Canvas { Width = 800, Height = 600 };
        var host = new global::Avalonia.Controls.Window { Content = canvas, Width = 800, Height = 600 };
        var targets = (System.Collections.Generic.List<MandatoryVideoOverlay.Target>)typeof(MandatoryVideoOverlay)
            .GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MandatoryVideoOverlay.Instance)!;
        try
        {
            Point center;
            if (video)
            {
                host.Show();
                var target = new MandatoryVideoOverlay.Target(canvas, "x", 40, 999, new Random(1));
                targets.Add(target);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                center = target.GazeBounds.Center;
            }
            else
            {
                flash.Show();
                FlashOverlay.Active.Add((flash, new PixelRect(100, 100, 100, 100)));
                flash.Run(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
                var k = flash.DesktopScaling;
                center = new Point(150 / k, 150 / k);
            }
            gaze.EvaluateDesiredState();
            gaze.HandleGazeMove(center);
            gaze.Tick();
            Assert.InRange(Lock.Apply(center.X + 20, center.Y).X, center.X, center.X + 19.99);
        }
        finally
        {
            gaze.Stop(); Lock.Clear(); Lock.Apply(0, 0); targets.Clear(); host.Close(); flash.Close();
            WebcamTracker.Instance.Calibration = previousCalibration;
            FlashOverlay.Active.RemoveAll(x => x.Window == flash);
        }
    });

    [Fact]
    public void CalibrationKeepsItsOwnAttractor() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        Lock.Clear(); Lock.Apply(0, 0);
        var window = new WebcamCalibrationWindow();
        try
        {
            window.Show();
            Assert.True(WebcamCalibrationWindow.IsShowing);
            Lock.SetTarget(150, 150, 80);
            GazeFocus.SetCursorLock(null);
            GazeFocus.SetCursorLock(new Rect(1000, 1000, 100, 100));
            Assert.InRange(Lock.Apply(170, 150).X, 150, 169.99);
        }
        finally { window.Close(); Lock.Clear(); Lock.Apply(0, 0); }
    });
}
