using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using ConditioningControlPanel.Services.Bark;
using OpenCvSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebcamTongueTests
{
    private static readonly int[] Lip = { 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95, 78, 191, 80, 81, 82 };
    private static readonly Scalar Red = new(0, 0, 255), Green = new(0, 255, 0);

    // A 20 x 10 polygon. The rest of the frame must never affect the ratio.
    private static float[][] Landmarks()
    {
        var lm = new float[468][];
        for (int i = 0; i < lm.Length; i++) lm[i] = new float[2];
        for (int i = 0; i < Lip.Length; i++)
        {
            var corner = i / 5;
            lm[Lip[i]][0] = corner is 0 or 3 ? 4 : 23;
            lm[Lip[i]][1] = corner < 2 ? 4 : 13;
        }
        return lm;
    }

    private static Mat Frame(int redColumns)
    {
        var frame = new Mat(20, 30, MatType.CV_8UC3, Green);
        if (redColumns > 0) Cv2.Rectangle(frame, new Rect(4, 4, redColumns, 10), Red, -1);
        return frame;
    }

    [Fact]
    public void RatioUsesOnlyTheInnerLipAndExcludesTeethAndShadow()
    {
        var lm = Landmarks();
        using var frame = new Mat(20, 30, MatType.CV_8UC3, Red); // outside is ignored
        Cv2.Rectangle(frame, new Rect(4, 4, 5, 10), Green, -1);
        Cv2.Rectangle(frame, new Rect(9, 4, 5, 10), new Scalar(255, 255, 255), -1);
        Cv2.Rectangle(frame, new Rect(14, 4, 5, 10), new Scalar(0, 0, 0), -1);
        Assert.Equal(.5, WebcamTracker.ComputeTongueRatio(frame, lm), 3);
        Cv2.Rectangle(frame, new Rect(19, 4, 5, 10), new Scalar(255, 255, 255), -1);
        Cv2.Rectangle(frame, new Rect(4, 4, 5, 10), new Scalar(0, 0, 0), -1);
        Assert.Equal(0, WebcamTracker.ComputeTongueRatio(frame, lm)); // no valid pixels
        Cv2.Rectangle(frame, new Rect(4, 4, 1, 7), Red, -1);
        Assert.Equal(0, WebcamTracker.ComputeTongueRatio(frame, lm)); // fewer than eight valid pixels
        Cv2.Rectangle(frame, new Rect(4, 4, 1, 8), Red, -1);
        Assert.Equal(1, WebcamTracker.ComputeTongueRatio(frame, lm));
        using var hsv = new Mat(20, 30, MatType.CV_8UC3, new Scalar(170, 255, 255));
        using var wrapRed = new Mat();
        Cv2.CvtColor(hsv, wrapRed, ColorConversionCodes.HSV2BGR);
        foreach (var index in Lip) lm[index][0] -= 5; // clipped at the left edge
        Assert.Equal(1, WebcamTracker.ComputeTongueRatio(wrapRed, lm));
        foreach (var index in Lip) { lm[index][0] = 1; lm[index][1] = 1; }
        Assert.Equal(0, WebcamTracker.ComputeTongueRatio(frame, lm)); // degenerate polygon
    }

    [Fact]
    public void RequiresOpenMouthAndHeldTongue_UsesHysteresisCooldownAndFaceReset() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        var lm = Landmarks();
        using var enter = Frame(6);  // .30 > .28
        using var hold = Frame(4);   // .20 > .18, but below entry
        using var leave = Frame(2);  // .10 < .18
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(tracker.ProcessTongueFrame(enter, lm, false, t));
        Assert.False(tracker.ProcessTongueFrame(hold, lm, true, t));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t));
        Assert.False(tracker.ProcessTongueFrame(hold, lm, true, t.AddMilliseconds(349)));
        Assert.True(tracker.ProcessTongueFrame(hold, lm, true, t.AddMilliseconds(350)));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(400))); // one per hold
        Assert.False(tracker.ProcessTongueFrame(leave, lm, true, t.AddMilliseconds(401)));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(402)));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(752))); // cooldown
        Assert.True(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(1050)));
        tracker.NoFace();
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(2000)));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, false, t.AddMilliseconds(2350)));
        Assert.False(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(3000)));
        Assert.True(tracker.ProcessTongueFrame(enter, lm, true, t.AddMilliseconds(3350)));
    });

    [Fact]
    public Task DetectorEventsReachDevicesOnce_DetachDropsThem_AndStartResetsCount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var tracker = WebcamTracker.Instance;
        var type = typeof(WebcamTracker);
        var runField = type.GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastFire = type.GetField("_lastTongueOut", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousFire = lastFire.GetValue(tracker);
        var mouth = (MouthOpenDetector)type.GetField("_mouth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
        var process = type.GetMethod("ProcessTongueLandmarks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var run = Activator.CreateInstance(type.GetNestedType("Run", BindingFlags.NonPublic)!, nonPublic: true)!;
        var models = WebcamTracker.ModelDir;
        var availability = CoreWebcam.IsAvailableProvider;
        CoreWebcam.IsAvailableProvider = () => true;
        var section = new DevicesSettingsSection();
        var host = new global::Avalonia.Controls.Window { Content = section };
        var log = section.FindControl<TextBlock>("TxtWebcamDebugLog")!;
        var button = section.FindControl<Button>("BtnWebcamDebugStart")!;
        using var frame = Frame(20);
        var lm = Landmarks();
        var t = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        void OpenMouth()
        {
            mouth.Reset();
            for (int i = 0; i < 18; i++) mouth.Update(.1, t.AddMilliseconds(i), .5);
            mouth.Update(.6, t.AddMilliseconds(20), .5);
            mouth.Update(.6, t.AddMilliseconds(21), .5);
            Assert.True(mouth.IsOpen);
        }
        void Feed(DateTime at) => process.Invoke(tracker, new object[] { frame, lm, at, run });
        try
        {
            host.Show();
            runField.SetValue(tracker, run); // simulate the current capture run; no camera is opened
            tracker.NoFace();
            OpenMouth();
            Feed(t); Feed(t.AddMilliseconds(350));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Loc.GetF("webcam_debug_tongue_out_fmt", 1), log.Text);
            var before = log.Text;
            host.Content = null;
            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(2)); Feed(t.AddMilliseconds(2350));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, log.Text);
            host.Content = section;
            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(4)); Feed(t.AddMilliseconds(4350));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Loc.GetF("webcam_debug_tongue_out_fmt", 2), log.Text);
            Assert.DoesNotContain(Loc.GetF("webcam_debug_tongue_out_fmt", 3), log.Text);

            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(6)); Feed(t.AddMilliseconds(6350));
            runField.SetValue(tracker, null); // stopped before dispatch: stale capture cannot publish
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(Loc.GetF("webcam_debug_tongue_out_fmt", 3), log.Text);

            CoreSettings.Current.WebcamConsentGiven = true;
            CoreSettings.Current.WebcamConsentVersion = WebcamConsent.ConsentVersion;
            WebcamTracker.ModelDir = System.IO.Path.Combine(TestUserDataProfile.Root, "absent-tongue-models");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!button.IsEnabled) { Assert.True(DateTime.UtcNow < deadline); await Task.Delay(10); }
            log.Text = "";
            runField.SetValue(tracker, run);
            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(8)); Feed(t.AddMilliseconds(8350));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(Loc.GetF("webcam_debug_tongue_out_fmt", 1), log.Text);
        }
        finally
        {
            runField.SetValue(tracker, null);
            tracker.NoFace();
            mouth.Reset();
            lastFire.SetValue(tracker, previousFire);
            WebcamTracker.ModelDir = models;
            CoreWebcam.IsAvailableProvider = availability;
            host.Close();
        }
    });
    [Fact] // WPF BarkService.cs:653-663: a held tongue event raises the companion rule
    public void CurrentTongueEventReachesBarkGate_StoppedRunCannotSpeak() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = WebcamTracker.Instance;
        var type = typeof(WebcamTracker);
        var runField = type.GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lastFire = type.GetField("_lastTongueOut", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousFire = lastFire.GetValue(tracker);
        var mouth = (MouthOpenDetector)type.GetField("_mouth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
        var process = type.GetMethod("ProcessTongueLandmarks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var run = Activator.CreateInstance(type.GetNestedType("Run", BindingFlags.NonPublic)!, nonPublic: true)!;
        var s = CoreSettings.Current;
        var old = (BarkHead.Engine, s.AvatarEnabled, s.BarkChatSuppressionMs);
        var rotation = s.BarkVariantRotation.ToDictionary(x => x.Key, x => x.Value.ToList());
        var rule = new BarkRule { Id = "test-tongue", Trigger = "TongueOut", VariantPool = new() { new BarkVariant("tongue") } };
        var spoken = 0;
        var bark = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => spoken++ };
        using var frame = Frame(20);
        var lm = Landmarks();
        var t = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        void OpenMouth()
        {
            mouth.Reset();
            for (int i = 0; i < 18; i++) mouth.Update(.1, t.AddMilliseconds(i), .5);
            mouth.Update(.6, t.AddMilliseconds(20), .5);
            mouth.Update(.6, t.AddMilliseconds(21), .5);
            Assert.True(mouth.IsOpen);
        }
        void Feed(DateTime at) => process.Invoke(tracker, new object[] { frame, lm, at, run });
        try
        {
            s.AvatarEnabled = true; s.BarkChatSuppressionMs = 0;
            bark.Start(); BarkHead.Engine = bark;
            lastFire.SetValue(tracker, DateTime.MinValue);
            runField.SetValue(tracker, run);
            tracker.NoFace(); OpenMouth();
            Feed(t); Feed(t.AddMilliseconds(350));
            runField.SetValue(tracker, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, spoken);

            runField.SetValue(tracker, run);
            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(2)); Feed(t.AddMilliseconds(2350));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, spoken);
            tracker.NoFace(); OpenMouth();
            Feed(t.AddSeconds(4)); Feed(t.AddMilliseconds(4350));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, spoken); // existing bark gate enforces its 60-second gap
        }
        finally
        {
            runField.SetValue(tracker, null); tracker.NoFace(); mouth.Reset();
            lastFire.SetValue(tracker, previousFire);
            s.BarkVariantRotation = rotation;
            (BarkHead.Engine, s.AvatarEnabled, s.BarkChatSuppressionMs) = old;
        }
    });

}
