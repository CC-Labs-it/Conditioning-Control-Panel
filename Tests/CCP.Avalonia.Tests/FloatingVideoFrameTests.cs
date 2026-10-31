using System.Linq;
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
            _ = new FloatingVideoFrame(w, () => now);
            w.Show();
            var loop = Assert.Single(BeatLoop.Running.Where(l => l.Owner == w));
            var source = TopLevelFrameSource.For(w);
            Assert.True(loop.OnFrameBeat);
            Assert.Equal(1, source.SubscriberCount);
            w.MouseDown(new Point(20, 20), MouseButton.Left);
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
            w.Close();
            Assert.False(loop.IsRunning);
            Assert.DoesNotContain(loop, BeatLoop.Running);
            Assert.Equal(0, source.SubscriberCount);
        }
        finally { w.Close(); (s.MotionLevel, s.MandatoryVideoFloatingSpeedPercent) = saved; }
        return Task.CompletedTask;
    });

}
