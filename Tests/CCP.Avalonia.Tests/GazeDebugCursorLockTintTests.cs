using System;
using Avalonia;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GazeDebugCursorLockTintTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("cooldown")]
    [InlineData("face")]
    [InlineData("miss")]
    [InlineData("stop")]
    public void BubbleLockAndReleaseTintCursor(string release) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var tracker = WebcamTracker.Instance;
        var calibration = tracker.Calibration;
        tracker.Calibration = null;
        var make = GazeDebugCursor.MakeClickThrough;
        GazeDebugCursor.MakeClickThrough = _ => true;
        CoreSettings.Current.FlashGazePopEnabled = CoreSettings.Current.FlashGazeLingerEnabled = CoreSettings.Current.VideoGazeClickEnabled = false;
        CoreSettings.Current.RestrictGazeContentToCalibratedScreen = false;
        var gaze = new GazeFocus { Clock = new Clock(), CanRun = () => true, PopBubble = _ => { } };
        var bubble = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(bubble);
        try
        {
            GazeDebugCursor.ShowCursor();
            var cursor = Assert.IsType<GazeDebugCursor>(GazeDebugCursor.Active);
            var pink = Color.FromRgb(255, 105, 180); var gold = Color.FromRgb(255, 208, 128);
            void Check(bool locked, Color color) { Assert.Equal(locked, cursor.Locked); Assert.Equal(color, cursor.BodyColor); Assert.Equal(color, cursor.TrailColor); Assert.Equal(color, cursor.BloomColor); }
            gaze.MasterEnabled = true;
            gaze.Tick(); Check(false, pink); // no gaze
            gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick(); Check(true, gold);
            switch (release)
            {
                case "cooldown": gaze.HandleBlink(); break;
                case "face": gaze.HandleFaceLost(); break;
                case "miss": bubble.Popping = true; break;
                case "stop": gaze.Stop(); break;
            }
            if (release != "stop") gaze.Tick();
            Check(false, pink);
            Assert.Equal(pink, new GazeCursorTrailControl().TrailColor);
        }
        finally
        {
            gaze.Stop(); GazeDebugCursor.CloseActive(); GazeDebugCursor.MakeClickThrough = make;
            tracker.Calibration = calibration; BubbleOverlay.Field.Bubbles.Remove(bubble);
        }
    });

    [Fact]
    public void RefineChipLocksAndExitClearsTint() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var tracker = WebcamTracker.Instance; var calibration = tracker.Calibration; tracker.Calibration = null;
        var make = GazeDebugCursor.MakeClickThrough; GazeDebugCursor.MakeClickThrough = _ => true;
        CoreSettings.Current.FlashGazePopEnabled = CoreSettings.Current.FlashGazeLingerEnabled = CoreSettings.Current.VideoGazeClickEnabled = false;
        CoreSettings.Current.RestrictGazeContentToCalibratedScreen = false;
        var clock = new Clock(); var gaze = new GazeFocus { Clock = clock, CanRun = () => true, RaiseRefine = _ => { } };
        var a = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var b = new AmbientBubble { X = 130, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(a); BubbleOverlay.Field.Bubbles.Add(b);
        GazeRefineOverlay? panel = null;
        gaze.MakeRefine = (choices, bounds) => { panel = new GazeRefineOverlay(choices, bounds, new Rect(0, 0, 1000, 800), 1); panel.Show(); return panel; };
        try
        {
            GazeDebugCursor.ShowCursor(); var cursor = Assert.IsType<GazeDebugCursor>(GazeDebugCursor.Active);
            gaze.MasterEnabled = true; gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick();
            clock.Now = clock.Now.AddMilliseconds(600); gaze.Tick();
            Assert.NotNull(panel); Assert.False(cursor.Locked);
            gaze.HandleGazeMove(panel.ChipBounds(0).Center); gaze.Tick();
            Assert.True(cursor.Locked); Assert.Equal(Color.FromRgb(255, 208, 128), cursor.TrailColor);
            b.Popping = true; gaze.Tick(); Assert.False(cursor.Locked); Assert.Equal(Color.FromRgb(255, 105, 180), cursor.BodyColor);
        }
        finally
        {
            gaze.Stop(); panel?.Close(); GazeDebugCursor.CloseActive(); GazeDebugCursor.MakeClickThrough = make;
            tracker.Calibration = calibration; BubbleOverlay.Field.Bubbles.Remove(a); BubbleOverlay.Field.Bubbles.Remove(b);
        }
    });
}
