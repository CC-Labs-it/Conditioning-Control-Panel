using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FloatingVideoFrameTests
{
    [Fact]
    public Task Drag_is_one_to_one_and_speed_updates_live() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent);
        var w = new Window { Width = 300, Height = 180, Content = new Border { Background = global::Avalonia.Media.Brushes.Black } };
        try
        {
            s.MotionLevel = MotionLevel.Off;
            s.MandatoryVideoFloatingSpeedPercent = 20;
            w.Show();
            w.Position = new PixelPoint(100, 100);
            var frame = new FloatingVideoFrame(w);
            Dispatcher.UIThread.RunJobs();
            var before = w.Position;
            w.MouseDown(new Point(20, 20), MouseButton.Left);
            w.MouseMove(new Point(60, 40));
            Assert.Equal(before.X + (int)(40 * w.RenderScaling), w.Position.X);
            Assert.Equal(before.Y + (int)(20 * w.RenderScaling), w.Position.Y);
            w.MouseUp(new Point(20, 20), MouseButton.Left);
            frame.Tick();
            Assert.Equal(.2, frame.MotionSpeed);
            s.MandatoryVideoFloatingSpeedPercent = 60;
            frame.Tick();
            Assert.Equal(.6, frame.MotionSpeed);
        }
        finally { w.Close(); (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent) = saved; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Release_samples_movement_since_the_last_move_event() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent);
        var w = new Window { Width = 300, Height = 180, Content = new Border { Background = global::Avalonia.Media.Brushes.Black } };
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.MandatoryVideoFloatingSpeedPercent = 100;
            w.Show();
            w.Position = new PixelPoint(100, 100);
            double now = 0;
            var frame = new FloatingVideoFrame(w, () => now);
            w.MouseDown(new Point(20, 20), MouseButton.Left);
            now = 20;
            w.MouseMove(new Point(21, 20)); // This movement alone is a tap, with no throw.
            var beforeRelease = w.Position;
            now = 40;
            w.MouseUp(new Point(120, 20), MouseButton.Left);
            now = 50;
            frame.Tick();
            Assert.True(w.Position.X > beforeRelease.X + 50 * w.RenderScaling,
                "movement in the release event must contribute to the throw");
        }
        finally { w.Close(); (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent) = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Motion_uses_the_shared_beat_and_unsubscribes_when_parked_or_closed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent);
        var w = new Window { Width = 300, Height = 180, Content = new Border { Background = global::Avalonia.Media.Brushes.Black } };
        try
        {
            s.MotionLevel = MotionLevel.Full;
            s.MandatoryVideoFloatingSpeedPercent = 100;
            w.Position = new PixelPoint(100, 100);
            double now = 0;
            int clockReads = 0;
            var frame = new FloatingVideoFrame(w, () => { clockReads++; return now; });
            w.Show();
            var source = TopLevelFrameSource.For(w);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == w);
            Assert.Equal(0, source.SubscriberCount);
            var area = w.Screens.Primary!.WorkingArea;
            w.Position = new PixelPoint(area.Right - 10, area.Y + 100);
            w.Width = 400; Dispatcher.UIThread.RunJobs();
            Assert.True(w.Position.X + w.Bounds.Width * w.RenderScaling <= area.Right);
            Assert.Equal(0, source.SubscriberCount);
            w.Width = 300; w.Position = new PixelPoint(100, 100);
            Dispatcher.UIThread.RunJobs();
            clockReads = 0; frame.Tick(); BeatLoop.StepAll();
            Assert.Equal(0, clockReads);
            w.MouseDown(new Point(20, 20), MouseButton.Left);
            var loop = Assert.Single(BeatLoop.Running.Where(l => l.Owner == w));
            Assert.True(loop.OnFrameBeat);
            Assert.Equal(1, source.SubscriberCount);
            now = 20; w.MouseMove(new Point(100, 20));
            now = 40; w.MouseUp(new Point(120, 20), MouseButton.Left);
            var before = w.Position;
            now = 80; BeatLoop.StepAll();
            Assert.True(w.Position.X > before.X, "the shared beat must advance a throw");
            w.Hide();
            Assert.False(loop.IsTicking);
            Assert.Equal(0, source.SubscriberCount);
            w.Show();
            Assert.True(loop.OnFrameBeat);
            w.WindowState = WindowState.Minimized;
            Assert.False(loop.IsTicking);
            Assert.Equal(0, source.SubscriberCount);
            w.WindowState = WindowState.Normal;
            Assert.True(loop.OnFrameBeat);
            for (var i = 0; i < 1000 && loop.IsRunning; i++)
            { now += 50; BeatLoop.StepAll(); }
            Assert.False(loop.IsRunning);
            Assert.Equal(0, source.SubscriberCount);
            clockReads = 0; frame.Tick(); BeatLoop.StepAll();
            Assert.Equal(0, clockReads);
            w.MouseDown(new Point(20, 20), MouseButton.Left);
            Assert.True(loop.OnFrameBeat);
            w.Close();
            Assert.False(loop.IsRunning);
            Assert.DoesNotContain(loop, BeatLoop.Running);
            Assert.Equal(0, source.SubscriberCount);
        }
        finally { w.Close(); (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent) = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Capture_loss_places_the_rehomed_window_before_the_beat_stops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var w = new Window { Width = 300, Height = 180, Content = new Border { Background = global::Avalonia.Media.Brushes.Black } };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var area = w.Screens.Primary!.WorkingArea;
            w.Position = new PixelPoint(area.X + 100, area.Y + 100);
            var frame = new FloatingVideoFrame(w);
            var motion = Assert.IsType<FloatingVideoMotion>(typeof(FloatingVideoFrame)
                .GetField("_motion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(frame));
            // A held window can straddle this work area on a desktop extending to the left.
            motion.SetDesktop(area.X - area.Width, area.Y, area.Width * 2, area.Height);
            IPointer? pointer = null;
            w.PointerPressed += (_, e) => pointer = e.Pointer;
            w.MouseDown(new Point(20, 20), MouseButton.Left);
            Assert.True(motion.Held);
            w.MouseMove(new Point(-180, 20));
            Assert.Equal(area.X - 100, w.Position.X);
            Assert.NotNull(pointer);
            pointer!.Capture(null);
            Assert.False(motion.Active);
            Assert.Equal((double)area.X, motion.X);
            Assert.Equal(area.X, w.Position.X);
            Assert.Equal(0, TopLevelFrameSource.For(w).SubscriberCount);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == w);
            frame.Tick();
            Assert.Equal(area.X, w.Position.X);
        }
        finally { w.MouseUp(new Point(20, 20), MouseButton.Left); w.Close(); }
        return Task.CompletedTask;
    });

}
