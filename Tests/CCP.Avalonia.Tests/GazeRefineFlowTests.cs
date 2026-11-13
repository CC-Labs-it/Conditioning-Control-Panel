using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class GazeRefineFlowTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        internal void Advance(int ms) => Now = Now.AddMilliseconds(ms);
    }
    [Theory]
    [InlineData("select")]
    [InlineData("away")]
    [InlineData("face")]
    [InlineData("timeout")]
    [InlineData("expired")]
    [InlineData("stop")]
    public void RefineOwnsDwellUntilSelectionOrCancellation(string exit) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var settings = CoreSettings.Current;
        settings.BubbleGazePopEnabled = true;
        settings.FlashGazePopEnabled = settings.FlashGazeLingerEnabled = settings.VideoGazeClickEnabled = false;
        settings.RestrictGazeContentToCalibratedScreen = false;
        var tracker = WebcamTracker.Instance; var calibration = tracker.Calibration; tracker.Calibration = null;
        var clock = new Clock(); var popped = new List<AmbientBubble>();
        var gaze = new GazeFocus { Clock = clock, CanRun = () => true, PopBubble = b => { popped.Add(b); b.Popping = true; } };
        var a = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var b = new AmbientBubble { X = 130, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(a); BubbleOverlay.Field.Bubbles.Add(b);
        GazeRefineOverlay? panel = null; int openings = 0;
        gaze.MakeRefine = (choices, bounds) => { openings++; panel = new GazeRefineOverlay(choices, bounds, new Rect(0, 0, 1000, 800), 1); panel.Show(); return panel; };
        try
        {
            gaze.MasterEnabled = true; gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick();
            clock.Advance(600); gaze.Tick();
            Assert.NotNull(panel); Assert.True(panel.IsVisible); Assert.Empty(popped);
            gaze.HandleBlink(); Assert.Empty(popped);
            if (exit == "select")
            {
                var competitor = new Window { Topmost = true }; competitor.Show();
                gaze.HandleGazeMove(new Point(3000, 3000)); int raises = 0; gaze.RaiseRefine = p => { Assert.Same(panel, p); raises++; };
                gaze.Tick(); Assert.Equal(1, raises);
                clock.Advance(199); gaze.Tick(); Assert.Equal(1, raises);
                clock.Advance(1); gaze.Tick(); Assert.Equal(2, raises); competitor.Close();
                var chip = panel.ChipBounds(1).Center;
                var attractor = (GazeLock)typeof(WebcamTracker).GetField("_gazeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
                attractor.Clear(); attractor.Apply(0, 0);
                gaze.HandleGazeMove(chip); gaze.Tick();
                Assert.NotEqual(chip.X + 20, attractor.Apply(chip.X + 20, chip.Y).X);
                clock.Advance(549); gaze.Tick(); Assert.Empty(popped);
                clock.Advance(1); gaze.Tick();
                Assert.Equal(new[] { a }, popped); Assert.False(panel.IsVisible);
                Assert.Equal((chip.X + 20, chip.Y), attractor.Apply(chip.X + 20, chip.Y));
            }
            else if (exit == "expired")
            {
                b.Popping = true; gaze.Tick(); Assert.False(panel.IsVisible); Assert.Empty(popped);
            }
            else if (exit == "stop")
            {
                gaze.Stop(); Assert.False(panel.IsVisible); Assert.Empty(popped);
            }
            else
            {
                if (exit == "away") { gaze.HandleGazeMove(new Point(3000, 3000)); gaze.Tick(); clock.Advance(700); }
                if (exit == "face") { gaze.HandleFaceLost(); gaze.Tick(); clock.Advance(1200); }
                if (exit == "timeout") clock.Advance(6000);
                gaze.Tick(); Assert.False(panel.IsVisible); Assert.Empty(popped);
                // Cancellation permits the next normal dwell instead of opening the panel again.
                gaze.HandleFaceFound(); gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick();
                clock.Advance(600); gaze.Tick(); Assert.Single(popped); Assert.Equal(1, openings);
            }
        }
        finally { gaze.Stop(); panel?.Close(); BubbleOverlay.Field.Bubbles.Remove(a); BubbleOverlay.Field.Bubbles.Remove(b); tracker.Calibration = calibration; }
    });
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RefineCanActivateFlashOrVideo(bool video, bool clicked) => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var settings = CoreSettings.Current;
        settings.BubbleGazePopEnabled = true; settings.FlashGazePopEnabled = !video;
        settings.FlashGazeLingerEnabled = false; settings.VideoGazeClickEnabled = video;
        settings.RestrictGazeContentToCalibratedScreen = false; settings.FlashExitStyle = FlashExitStyle.None;
        var tracker = WebcamTracker.Instance; var previous = tracker.Calibration; tracker.Calibration = null;
        var clock = new Clock(); var gaze = new GazeFocus { Clock = clock, CanRun = () => true };
        var canvas = new Canvas { Width = 800, Height = 600 };
        var host = new Window { Width = 800, Height = 600, Content = canvas };
        var targets = (List<MandatoryVideoOverlay.Target>)typeof(MandatoryVideoOverlay)
            .GetField("_targets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MandatoryVideoOverlay.Instance)!;
        var flash = new FlashOverlayWindow(); MandatoryVideoOverlay.Target? target = null;
        GazeRefineOverlay? panel = null; AmbientBubble? bubble = null; int hits = 0;
        gaze.MakeRefine = (choices, bounds) => { panel = new GazeRefineOverlay(choices, bounds, new Rect(0, 0, 1000, 800), 1); panel.Show(); return panel; };
        try
        {
            Rect source;
            if (video)
            {
                host.Show(); target = new MandatoryVideoOverlay.Target(canvas, "x", 40, 999, new Random(1)); target.OnHit = () => { hits++; targets.Remove(target); };
                targets.Add(target); Dispatcher.UIThread.RunJobs(); source = target.GazeBounds;
            }
            else
            {
                flash.Show(); FlashOverlay.Active.Add((flash, new PixelRect(100, 100, 100, 100)));
                flash.Run(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
                var scale = flash.DesktopScaling; source = new Rect(100 / scale, 100 / scale, 100 / scale, 100 / scale);
            }
            bubble = new AmbientBubble { X = source.X, Y = source.Y, Size = Math.Max(source.Width, source.Height), Clickable = true };
            BubbleOverlay.Field.Bubbles.Add(bubble);
            gaze.MasterEnabled = true; gaze.HandleGazeMove(source.Center); gaze.Tick(); clock.Advance(600); gaze.Tick();
            Assert.NotNull(panel); Assert.True(panel.IsVisible);
            if (clicked) { target!.Hit(); Assert.True(target.GazeBounds.Width > 0); gaze.Tick(); Assert.False(panel.IsVisible); Assert.Equal(1, hits); return; }
            gaze.HandleGazeMove(panel.ChipBounds(video ? 1 : 0).Center); gaze.Tick(); clock.Advance(550); gaze.Tick();
            Assert.False(panel.IsVisible);
            if (video) Assert.Equal(1, hits);
            else Assert.DoesNotContain(FlashOverlay.Active, x => ReferenceEquals(x.Window, flash));
        }
        finally
        {
            gaze.Stop(); panel?.Close(); if (target != null) targets.Remove(target); host.Close();
            FlashOverlay.Active.RemoveAll(x => ReferenceEquals(x.Window, flash)); flash.Close();
            if (bubble != null) BubbleOverlay.Field.Bubbles.Remove(bubble); tracker.Calibration = previous;
        }
    });
    [Fact]
    public void RefusedPanelFallsBackToTheFirstStageSelection() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var settings = CoreSettings.Current;
        settings.BubbleGazePopEnabled = true;
        settings.FlashGazePopEnabled = settings.FlashGazeLingerEnabled = settings.VideoGazeClickEnabled = false;
        settings.RestrictGazeContentToCalibratedScreen = false;
        var tracker = WebcamTracker.Instance; var previous = tracker.Calibration; tracker.Calibration = null;
        var clock = new Clock(); int popped = 0;
        var gaze = new GazeFocus { Clock = clock, CanRun = () => true, PopBubble = _ => popped++, MakeRefine = (_, _) => null };
        var a = new AmbientBubble { X = 100, Y = 100, Size = 100, Clickable = true };
        var b = new AmbientBubble { X = 130, Y = 100, Size = 100, Clickable = true };
        BubbleOverlay.Field.Bubbles.Add(a); BubbleOverlay.Field.Bubbles.Add(b);
        try
        {
            gaze.MasterEnabled = true; gaze.HandleGazeMove(new Point(150, 150)); gaze.Tick();
            clock.Advance(600); gaze.Tick(); Assert.Equal(1, popped);
        }
        finally { gaze.Stop(); BubbleOverlay.Field.Bubbles.Remove(a); BubbleOverlay.Field.Bubbles.Remove(b); tracker.Calibration = previous; }
    });
    [Fact]
    public void ChipKindsAndDwellArcRender() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var flash = new FlashOverlayWindow();
        var panel = new GazeRefineOverlay(new object[] { new AmbientBubble(), flash, new object() }, new[] { new Rect(300, 300, 10, 10), new Rect(340, 300, 40, 40), new Rect(380, 300, 100, 100) }, new Rect(0, 0, 1400, 900), 1);
        try
        {
            var canvas = (Canvas)panel.Content!; var clock = new TestAnimationClock(); clock.Attach(canvas);
            Assert.Equal(0, canvas.Opacity);
            panel.Show(); Dispatcher.UIThread.RunJobs(); clock.Step(0); clock.Step(65);
            Assert.InRange(canvas.Opacity, .49, .51); clock.Step(130); Assert.Equal(1, canvas.Opacity, 3);
            var children = canvas.Children;
            Assert.IsType<Ellipse>(children[2]); Assert.IsType<Border>(children[4]);
            Assert.IsType<ImageBrush>(((Border)children[4]).Background); Assert.IsType<Polygon>(((Border)children[6]).Child);
            Assert.NotEqual(panel.ChipBounds(0).Width, panel.ChipBounds(2).Width);
            using var before = new MemoryStream(); using var after = new MemoryStream();
            panel.CaptureRenderedFrame()!.Save(before); panel.Progress(1, .5); Dispatcher.UIThread.RunJobs(); panel.CaptureRenderedFrame()!.Save(after);
            Assert.False(before.ToArray().SequenceEqual(after.ToArray()));
            Assert.IsType<PathGeometry>(((global::Avalonia.Controls.Shapes.Path)children[5]).Data);
        }
        finally { panel.Close(); flash.Close(); }
    });
    [Fact]
    public void LayoutStaysOnNegativeOriginMonitorAndKeepsHitAreasApart() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var bounds = new[] { new Rect(-1800, 250, 100, 100), new Rect(-1770, 250, 100, 100) };
        var panel = new GazeRefineOverlay(new object[] { new object(), new object() }, bounds, new Rect(-1920, 100, 1280, 720), 1.5);
        try
        {
            Assert.InRange(panel.ChipBounds(0).Left, -1920, -640);
            Assert.InRange(panel.ChipBounds(1).Right, -1920, -640);
            Assert.True(panel.ChipBounds(0).Center.X < panel.ChipBounds(1).Center.X);
            Assert.Equal(0, panel.HitTestChip(panel.ChipBounds(0).Center));
            Assert.Equal(1, panel.HitTestChip(panel.ChipBounds(1).Center));
            Assert.True(Math.Abs(panel.ChipBounds(0).Center.X - panel.ChipBounds(1).Center.X) >= (panel.ChipBounds(0).Width + panel.ChipBounds(1).Width) / 2 + 68);
            Assert.InRange(panel.DesiredPosition.X, -2880, -960);
        }
        finally { panel.Close(); }
    });
}
