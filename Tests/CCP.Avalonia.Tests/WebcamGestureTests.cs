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
        => Assert.Equal(mar, WebcamTracker.MouthAspectRatio(Lips((float)mar)), 5);

    [Fact]
    public void DegenerateMouthIsNotOpen()
    {
        var lm = Lips(0.6f); lm[308] = lm[78];
        Assert.Equal(0, WebcamTracker.MouthAspectRatio(lm));
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

    [Fact]
    public void RealPixelPathPublishesMouthThenTongueOnceAndFaceLossCancelsHold() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        int mouth = 0, tongue = 0;
        tracker.OnMouthOpen += () => mouth++;
        tracker.OnTongueOut += () => tongue++;
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, new Scalar(0, 0, 200));
        var now = new DateTime(2026, 1, 1);
        var sensitivity = CoreSettings.Current.WebcamSensitivity;
        try
        {
            CoreSettings.Current.WebcamSensitivity = 0.5;
            for (int i = 0; i < 90; i++) tracker.ProcessMouth(bgr, Lips(0.1f), now.AddMilliseconds(i * 33));
            for (int i = 90; i < 150; i++) tracker.ProcessMouth(bgr, Lips(0.6f), now.AddMilliseconds(i * 33));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, mouth); Assert.Equal(1, tongue);
            tracker.NoFace();
            tracker.ProcessMouth(bgr, Lips(0.6f), now.AddSeconds(10));
            Dispatcher.UIThread.RunJobs();
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

    [Fact]
    public void CalibrationMouthPromptsCompleteFromRealDetector() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var window = new WebcamCalibrationWindow();
        var phase = typeof(WebcamCalibrationWindow).GetMethod("RunValidationPhaseAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var tracker = WebcamTracker.Instance;
        using var bgr = new Mat(40, 40, MatType.CV_8UC3, Scalar.All(0));
        var saved = (CoreSettings.Current.WebcamSensitivity, CoreSettings.Current.MasterVolume);
        try
        {
            CoreSettings.Current.WebcamSensitivity = 0.5;
            CoreSettings.Current.MasterVolume = 0;
            var task = (Task)phase.Invoke(window, null)!;
            var prompt = window.FindControl<TextBlock>("TxtValidationPrompt")!;
            var detail = window.FindControl<TextBlock>("TxtValidationDetail")!;
            async Task WaitFor(Func<bool> condition)
            {
                for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
                Assert.True(condition());
            }
            await WaitFor(() => prompt.Text == "Blink a couple of times");
            tracker.RaiseBlinkForTest(); tracker.RaiseBlinkForTest();
            await WaitFor(() => prompt.Text == "Open your mouth wide");
            var now = DateTime.UtcNow.AddMinutes(-2); // leave singleton cooldowns in the past for later tests
            void OpenMouth()
            {
                tracker.NoFace();
                for (int i = 0; i < 90; i++) tracker.ProcessMouth(bgr, Lips(0.1f), now.AddMilliseconds(i * 33));
                for (int i = 90; i < 110; i++) tracker.ProcessMouth(bgr, Lips(0.6f), now.AddMilliseconds(i * 33));
                now = now.AddSeconds(10);
            }
            OpenMouth();
            await WaitFor(() => detail.Text == "Detected."); // success, before the 5 s timeout
            await WaitFor(() => prompt.Text == "Open your mouth wide again");
            OpenMouth();
            await WaitFor(() => detail.Text == "Detected.");
            await task;
        }
        finally
        {
            (CoreSettings.Current.WebcamSensitivity, CoreSettings.Current.MasterVolume) = saved;
            window.Close();
        }
    }).GetAwaiter().GetResult();
}
