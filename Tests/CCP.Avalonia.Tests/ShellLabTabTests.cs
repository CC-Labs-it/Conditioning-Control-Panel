using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellLabTabTests
{
    // Avalonia 12 makes Screen's constructor internal; clone a headless screen and set its metadata.
    private static Screen Display(Screen template, double scale, PixelRect bounds, bool primary)
    {
        var screen = (Screen)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(template, null)!;
        void Set(string name, object value) => typeof(Screen).GetProperty(name)!.SetValue(screen, value);
        Set(nameof(Screen.Scaling), scale); Set(nameof(Screen.Bounds), bounds); Set(nameof(Screen.WorkingArea), bounds);
        Set(nameof(Screen.IsPrimary), primary); Set(nameof(Screen.DisplayName), $"display-{bounds.X}");
        return screen;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _now.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(int ms) => _now = _now.AddMilliseconds(ms);
    }

    [Fact]
    public void DiagnosticsDeduplicateGazeBoundLogAndUnsubscribeOnDetach() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var availability = CoreWebcam.IsAvailableProvider;
        CoreWebcam.IsAvailableProvider = () => true;
        var devices = new DevicesSettingsSection();
        var host = new Window { Content = devices };
        var tracker = WebcamTracker.Instance;
        var savedMonitor = CoreSettings.Current.WebcamCalibrationScreen;
        var savedRestriction = CoreSettings.Current.RestrictGazeContentToCalibratedScreen;
        try
        {
            CoreSettings.Current.WebcamCalibrationScreen = "unplugged-display";
            host.Show();
            var combo = devices.FindControl<ComboBox>("CmbWebcamMonitor")!;
            Assert.True(combo.IsEnabled);
            Assert.Equal("Primary", ((ComboBoxItem)combo.SelectedItem!).Tag);
            Assert.Equal("unplugged-display", CoreSettings.Current.WebcamCalibrationScreen); // seeding is not a save
            var choice = new ComboBoxItem { Content = "Display 2", Tag = "display-2" };
            combo.Items.Add(choice); combo.SelectedItem = choice;
            Assert.Equal("display-2", CoreSettings.Current.WebcamCalibrationScreen);
            CoreSettings.Current.RestrictGazeContentToCalibratedScreen = !savedRestriction;
            Assert.IsAssignableFrom<IAppSettingsSection>(devices).OnSectionShown();
            Assert.Equal(!savedRestriction, devices.FindControl<CheckBox>("ChkRestrictGazeToCalScreen")!.IsChecked);
            var counters = devices.FindControl<TextBlock>("TxtWebcamDebugCounters")!;
            var log = devices.FindControl<TextBlock>("TxtWebcamDebugLog")!;
            tracker.RaiseBlinkForTest();
            tracker.RaiseGazeSideForTest(GazeSide.Left);
            Assert.Equal("Face: — | Blinks: 1 | Gaze: Left", counters.Text);
            // Deliver the same UI-thread events as the capture loop, without opening a camera.
            void FaceEvent(string name) => ((Action)typeof(WebcamTracker).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tracker)!)();
            FaceEvent(nameof(WebcamTracker.OnFaceFound));
            Assert.Equal("Face: yes | Blinks: 1 | Gaze: Left", counters.Text);
            Assert.Contains("Face FOUND", log.Text);
            FaceEvent(nameof(WebcamTracker.OnFaceLost));
            Assert.Equal("Face: lost | Blinks: 1 | Gaze: Left", counters.Text);
            Assert.Contains("Face LOST", log.Text);
            var once = log.Text;
            tracker.RaiseGazeSideForTest(GazeSide.Left);
            Assert.Equal(once, log.Text);
            for (int i = 0; i < 20; i++) tracker.RaiseBlinkForTest();
            Assert.Equal(12, log.Text!.Split('\n').Length);
            Assert.Contains("Blink #21", log.Text);
            host.Content = null;
            var detached = log.Text;
            tracker.RaiseBlinkForTest();
            Assert.Equal(detached, log.Text);
            host.Content = devices;
            tracker.RaiseBlinkForTest();
            Assert.Contains("Blinks: 22", counters.Text); // reattach subscribes once
        }
        finally { host.Close(); CoreSettings.Current.WebcamCalibrationScreen = savedMonitor; CoreSettings.Current.RestrictGazeContentToCalibratedScreen = savedRestriction; CoreWebcam.IsAvailableProvider = availability; }
    });

    [Fact]
    public void MonitorFallbackAndGazeClampUseLiveScreenIdentityAndDpi() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var template = new Window().Screens.All.First();
        var first = Display(template, 1, new PixelRect(0, 0, 1920, 1080), true);
        var second = Display(template, 2, new PixelRect(-2560, 0, 2560, 1440), false);
        Screen[] screens = { second, first };
        Assert.Same(first, WebcamScreens.Resolve(screens, "Primary"));
        Assert.Same(first, WebcamScreens.Resolve(screens, "unplugged-display"));
        Assert.Same(second, WebcamScreens.Resolve(screens, WebcamScreens.Name(second)));
        Assert.Null(WebcamScreens.Resolve(Array.Empty<Screen>(), "Primary"));
        var s = CoreSettings.Current;
        var old = (s.RestrictGazeContentToCalibratedScreen, WebcamTracker.Instance.Calibration, WebcamScreens.Enumerate);
        try
        {
            s.RestrictGazeContentToCalibratedScreen = true;
            WebcamScreens.Enumerate = () => screens;
            WebcamTracker.Instance.Calibration = new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord { DeviceName = WebcamScreens.Name(second) } };
            Assert.True(GazeFocus.TargetOnCalibratedScreen(new Rect(-1100, 100, 100, 100)));
            Assert.False(GazeFocus.TargetOnCalibratedScreen(new Rect(100, 100, 100, 100)));
            var bubble = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
            int pops = 0;
            var gaze = new GazeFocus { CanRun = () => true, PopBubble = _ => pops++ };
            BubbleOverlay.Field.Bubbles.Add(bubble);
            try
            {
                gaze.MasterEnabled = true;
                gaze.HandleGazeMove(new Point(150, 150)); gaze.HandleBlink();
                Assert.Equal(0, pops); // actual selection excludes the other screen
                s.RestrictGazeContentToCalibratedScreen = false;
                gaze.HandleBlink();
                Assert.Equal(1, pops);
            }
            finally { gaze.Stop(); BubbleOverlay.Field.Bubbles.Remove(bubble); s.RestrictGazeContentToCalibratedScreen = true; }
            // The centre decides, so a target can straddle the edge.
            Assert.True(GazeFocus.TargetOnCalibratedScreen(new Rect(-75, 100, 100, 100)));
            WebcamScreens.Enumerate = () => new[] { first }; // unplugged after calibration: fail open
            Assert.True(GazeFocus.TargetOnCalibratedScreen(new Rect(100, 100, 100, 100)));
            WebcamTracker.Instance.Calibration = null;
            Assert.True(GazeFocus.TargetOnCalibratedScreen(new Rect(100, 100, 100, 100)));
        }
        finally { (s.RestrictGazeContentToCalibratedScreen, WebcamTracker.Instance.Calibration, WebcamScreens.Enumerate) = old; }
    });

    [Fact]
    public void FlashLingerIsIndependentOfClickAndPopAndGazePopNeverMultiplies() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGazeLingerExtensionMs,
            s.BubbleGazePopEnabled, s.VideoGazeClickEnabled, s.RestrictGazeContentToCalibratedScreen,
            s.FlashExitStyle, s.FlashClickable, s.CorruptionMode, s.HydraLimit, FlashOverlay.Clock, FlashOverlay.Multiply);
        var clock = new Clock();
        var gaze = new GazeFocus { Clock = clock, CanRun = () => true };
        var w = new FlashOverlayWindow();
        int multiplied = 0;
        try
        {
            (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashClickable) = (false, true, false);
            (s.BubbleGazePopEnabled, s.VideoGazeClickEnabled, s.RestrictGazeContentToCalibratedScreen) = (false, false, false);
            (s.FlashExitStyle, s.CorruptionMode, s.HydraLimit) = (FlashExitStyle.None, true, 10);
            s.FlashGazeLingerExtensionMs = 1500;
            FlashOverlay.Clock = clock;
            FlashOverlay.Multiply = (_, n, _, _) => multiplied += n;
            w.Show();
            FlashOverlay.Active.Add((w, new PixelRect(100, 100, 100, 100)));
            w.Run(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
            var originalDeadline = w.ExpiresAt;
            w.BoostLifetime(1);
            Assert.Equal(originalDeadline, w.ExpiresAt); // boosting never shortens a long lifetime
            clock.Advance(9500);
            gaze.EvaluateDesiredState();
            gaze.HandleGazeMove(new Point(150, 150));
            gaze.Tick();
            Assert.True(w.ExpiresAt > originalDeadline);
            Assert.Equal(1, w.DwellScale); // linger alone does not promise a pop
            var boosted = w.ExpiresAt;
            clock.Advance(249); gaze.Tick();
            Assert.Equal(boosted, w.ExpiresAt);
            clock.Advance(1); gaze.Tick();
            Assert.True(w.ExpiresAt > boosted);
            gaze.HandleBlink();
            Assert.Contains(FlashOverlay.Active, x => x.Window == w); // pop OFF also gates blink
            clock.Advance(GazeFocus.CooldownMs);
            s.FlashGazePopEnabled = true;
            s.FlashGazeLingerEnabled = false;
            gaze.Stop(); gaze.EvaluateDesiredState();
            gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick();
            clock.Advance(300); gaze.Tick();
            Assert.Equal(1.05, w.DwellScale, 3);
            clock.Advance(299); gaze.Tick();
            Assert.Contains(FlashOverlay.Active, x => x.Window == w);
            clock.Advance(1); gaze.Tick();
            Assert.DoesNotContain(FlashOverlay.Active, x => x.Window == w);
            Assert.Equal(0, multiplied); // WPF #784: gaze cannot sustain a hydra chain
        }
        finally
        {
            gaze.Stop(); w.Close(); FlashOverlay.Active.RemoveAll(x => x.Window == w);
            (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGazeLingerExtensionMs,
                s.BubbleGazePopEnabled, s.VideoGazeClickEnabled, s.RestrictGazeContentToCalibratedScreen,
                s.FlashExitStyle, s.FlashClickable, s.CorruptionMode, s.HydraLimit, FlashOverlay.Clock, FlashOverlay.Multiply) = old;
        }
    });

    [Fact]
    public void ExpiringFlashCannotBeRevivedAndBubbleGazeReachesBarkRules() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.BarkChatSuppressionMs, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled,
            s.RestrictGazeContentToCalibratedScreen, BarkHead.Engine);
        var rotation = s.BarkVariantRotation.ToDictionary(x => x.Key, x => x.Value.ToList());
        var w = new FlashOverlayWindow();
        var b = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var gaze = new GazeFocus { CanRun = () => true, PopBubble = _ => { } };
        try
        {
            w.Show(); w.Run(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            w.Expire(); var deadline = w.ExpiresAt;
            w.BoostLifetime(10000);
            Assert.True(w.IsFadingOut);
            Assert.Equal(deadline, w.ExpiresAt);
            s.AvatarEnabled = true; s.BarkChatSuppressionMs = 0;
            s.FlashGazePopEnabled = s.FlashGazeLingerEnabled = s.RestrictGazeContentToCalibratedScreen = false;
            var rule = new BarkRule { Id = "gaze-test", Trigger = "GazePopped", VariantPool = new() { new BarkVariant("pop") } };
            var bark = BarkHead.Engine = new BarkEngine(() => new BarkRuleSet(new[] { rule }));
            bark.Speak = _ => { };
            bark.Start();
            string? spoken = null;
            bark.BarkSpoken += (_, text) => spoken = text;
            BubbleOverlay.Field.Bubbles.Add(b);
            gaze.MasterEnabled = true;
            gaze.HandleGazeMove(new Point(150, 150)); gaze.HandleBlink();
            Assert.Equal("pop", spoken);
        }
        finally
        {
            gaze.Stop(); w.Close(); BubbleOverlay.Field.Bubbles.Remove(b);
            s.BarkVariantRotation = rotation;
            (s.AvatarEnabled, s.BarkChatSuppressionMs, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled,
                s.RestrictGazeContentToCalibratedScreen, BarkHead.Engine) = old;
        }
    });
}
