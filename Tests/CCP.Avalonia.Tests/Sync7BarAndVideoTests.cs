using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using System;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
namespace CCP.Avalonia.Tests;
public sealed class Sync7BarAndVideoTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public async Task AudioTransitionsResetBreathAndParkAndWakeTheClock()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            double now = 0; bool audible = false; var settings = CoreSettings.Current; var savedMotion = settings.MotionLevel;
            var border = new Border(); var window = new Window { Width = 300, Height = 200, Content = border };
            window.Show();
            var frame = new ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame(window, () => now, border, () => audible);
            try
            {
                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
                Assert.False(frame.Running); var brush = border.BorderBrush;
                now = 500; frame.Tick(); Assert.Same(brush, border.BorderBrush);
                audible = true; frame.Refresh(); Assert.True(frame.Running); Assert.Equal(0, frame.BreathSeconds);
                for (int i = 0; i < 24; i++) { now += 50; frame.Tick(); }
                Assert.Equal(1.2, frame.BreathSeconds, 5);
                audible = false; frame.Refresh();
                for (int i = 0; i < 7; i++) { now += 50; frame.Tick(); }
                Assert.False(frame.Running);
                audible = true; frame.Refresh(); Assert.Equal(0, frame.BreathSeconds);
                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off; global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                for (int i = 0; i < 4; i++) { now += 50; frame.Tick(); }
                Assert.False(frame.Running);
                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full; global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(frame.Running);
            }
            finally { window.Close(); settings.MotionLevel = savedMotion; }
            frame.Refresh(); Assert.False(frame.Running);
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task NativeDragPollsOutsideTheWindowAndReleases()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            double now = 0;
            var pointer = (new PixelPoint(110, 110), true);
            var window = new Window { Width = 200, Height = 120, Position = new PixelPoint(100, 100) };
            window.Show();
            var settings = CoreSettings.Current; var savedMotion = settings.MotionLevel;
            settings.MotionLevel = MotionLevel.Off;
            try
            {
                var frame = new ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame(window,
                    () => now, nativePointer: () => pointer);
                frame.StartDrag(pointer.Item1);
                pointer = (new PixelPoint(210, 160), true); now = 50; frame.Tick();
                Assert.Equal(new PixelPoint(200, 150), window.Position);
                pointer = (new PixelPoint(220, 170), false); now = 100; frame.Tick();
                var released = window.Position;
                Assert.Equal(new PixelPoint(210, 160), released);
                pointer = (new PixelPoint(300, 250), true); now = 150; frame.Tick();
                Assert.Equal(released, window.Position);
                Assert.Equal(1, ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame.PrimaryButtonKey(false));
                Assert.Equal(2, ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame.PrimaryButtonKey(true));
            }
            finally { window.Close(); settings.MotionLevel = savedMotion; }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task NativeMovesKeepTheirPathBetweenPolls()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            double now = 0;
            var pointer = (new PixelPoint(110, 110), true);
            var window = new Window { Width = 200, Height = 120, Position = new PixelPoint(100, 100) };
            window.Show();
            var settings = CoreSettings.Current; var savedMotion = settings.MotionLevel;
            settings.MotionLevel = MotionLevel.Full;
            try
            {
                var frame = new ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame(window,
                    () => now, nativePointer: () => pointer);
                frame.StartDrag(pointer.Item1);
                // A flick right, then left, all between the old 30 Hz polls.
                frame.HandleNativeMouse(0x0200, false, new PixelPoint(310, 110), 90);
                frame.HandleNativeMouse(0x0200, false, new PixelPoint(210, 110), 110);
                pointer = (new PixelPoint(500, 110), true); now = 115; frame.Tick();
                Assert.Equal(new PixelPoint(200, 100), window.Position);
                frame.HandleNativePointerUp(0x0202, false, new PixelPoint(190, 110), 120);
                Assert.Equal(new PixelPoint(180, 100), window.Position);
                now = 140; frame.Tick();
                Assert.True(window.Position.X < 180, "the recent leftward movement determines the throw");
                var released = window.Position;
                frame.HandleNativeMouse(0x0200, false, new PixelPoint(700, 110), 150);
                Assert.Equal(released, window.Position);
            }
            finally { window.Close(); settings.MotionLevel = savedMotion; }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task DragClockTicksEveryFrameAndReturnsToThirtyHertzOnRelease()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var pointer = (new PixelPoint(110, 110), true);
            var window = new Window { Width = 200, Height = 120, Position = new PixelPoint(100, 100) };
            window.Show();
            try
            {
                var frame = new ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame(window,
                    nativePointer: () => pointer);
                Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), frame.FrameInterval);
                frame.StartDrag(pointer.Item1);
                Assert.Equal(TimeSpan.Zero, frame.FrameInterval);
                frame.HandleNativePointerUp(0x0202, false, pointer.Item1, 10);
                Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), frame.FrameInterval);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(false, 0x0202, 0x0205)]
    [InlineData(true, 0x0205, 0x0202)]
    public async Task NativeUpUsesTheEventPositionAndTimeBeforeTheNextPoll(bool swapped, int primaryUp, int otherUp)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            double now = 0;
            var pointer = (new PixelPoint(110, 110), true);
            var window = new Window { Width = 200, Height = 120, Position = new PixelPoint(100, 100) };
            window.Show();
            var settings = CoreSettings.Current; var savedMotion = settings.MotionLevel;
            settings.MotionLevel = MotionLevel.Full;
            try
            {
                var frame = new ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame(window,
                    () => now, nativePointer: () => pointer);
                frame.StartDrag(pointer.Item1);
                pointer = (new PixelPoint(210, 160), true); now = 50; frame.Tick();
                // The mouse has already moved away when a delayed callback is delivered.
                pointer = (new PixelPoint(500, 400), false); now = 1000;
                frame.HandleNativePointerUp(otherUp, swapped, new PixelPoint(220, 170), 60);
                Assert.Equal(new PixelPoint(200, 150), window.Position);
                var eventMs = ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame.EventTime(60, 1000, now);
                frame.HandleNativePointerUp(primaryUp, swapped, new PixelPoint(220, 170), eventMs);
                Assert.Equal(new PixelPoint(210, 160), window.Position);
                Assert.True(frame.Running, "the throw uses the up time, not the later callback time");
                now = 1050; frame.Tick();
                Assert.NotEqual(new PixelPoint(490, 390), window.Position);
                Assert.Equal(995, ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame.EventTime(uint.MaxValue - 2, 2, 1000));
            }
            finally { window.Close(); settings.MotionLevel = savedMotion; }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task FloatingSurfaceAppliesPassiveStyleOnOpen()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var overlay = (ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay)Activator.CreateInstance(
                typeof(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay), nonPublic: true)!;
            typeof(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay)
                .GetField("<Floating>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(overlay, true);
            Assert.Equal((Action<Window>)X11Overlay.SetPassive, overlay.ApplyFloatingPassive);
            Window? passive = null;
            overlay.ApplyFloatingPassive = w => passive = w;
            var surface = overlay.Build(null, false);
            try { surface.Window.Show(); Assert.Same(surface.Window, passive); }
            finally { surface.Window.Close(); }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(true, "wl_surface", 1, true)]
    [InlineData(true, "XID", 1, false)]
    [InlineData(true, "Headless", 0, false)]
    [InlineData(false, "HWND", 1, false)]
    public void NativeLinuxDragBelongsToTheCompositor(bool linux, string descriptor, int handle, bool expected) =>
        Assert.Equal(expected, ConditioningControlPanel.Avalonia.Views.Overlays.FloatingVideoFrame.UsesCompositorDrag(
            linux, new PlatformHandle(new IntPtr(handle), descriptor)));
    [Theory]
    [InlineData(0, "label_session_bar_elapsed")]
    [InlineData(10, "label_session_bar_left")]
    public async Task SessionBarTracksClockPauseAndHiddenCountdown(int minutes, string template)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var savedCountdown = CoreSettings.Current.ShowSessionCountdown;
            var savedSessions = AvApp.Sessions;
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                CoreSettings.Current.ShowSessionCountdown = true;
                runner.Start(new Session { Id = "bar", Name = "Bar session", DurationMinutes = minutes });
                shell.OnSessionTick();
                var label = shell.Named<TextBlock>("TxtPresetsStatus")!;
                Assert.Equal(Loc.GetF(template, "Bar session", SessionClockLabel.Countdown(
                    minutes == 0 ? runner.Elapsed : runner.Remaining)), label.Text);
                runner.Pause();
                shell.SetPauseButton(true);
                Assert.EndsWith($" [{Loc.Get("label_paused")}]", label.Text);
                CoreSettings.Current.ShowSessionCountdown = false;
                shell.OnSessionTick();
                Assert.Equal($"Bar session [{Loc.Get("label_paused")}]", label.Text);
                runner.Stop(completed: false);
                shell.UpdateStartButton(force: true);
                Assert.Equal("", label.Text);
            }
            finally { try { if (runner.IsRunning) runner.Stop(completed: false); } finally { CoreSettings.Current.ShowSessionCountdown = savedCountdown; AvApp.Sessions = savedSessions; shell.Close(); } }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task FloatingFrameInsetsAndClipsVideo()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var video = new Panel();
            var window = new Window { Content = video };
            var frame = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.DecorateFloating(window);
            Assert.Same(video, frame.Child);
            Assert.Equal(new Thickness(4), frame.BorderThickness);
            Assert.Equal(new CornerRadius(10), frame.CornerRadius);
            video.Arrange(new Rect(0, 0, 200, 100));
            Assert.IsType<global::Avalonia.Media.RectangleGeometry>(video.Clip);
            window.Close();
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task FloatingSizeLoadsAndWritesTheSizeUsedByTheNextWindow()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var settings = CoreSettings.Current;
            var savedFloating = settings.MandatoryVideoFloating;
            var savedSize = settings.MandatoryVideoFloatingSizePercent;
            Window? host = null;
            try
            {
                settings.MandatoryVideoFloating = true;
                settings.MandatoryVideoFloatingSizePercent = 40;
                var control = new VideoFeatureControl();
                host = new Window { Width = 640, Height = 1000, Content = control };
                host.Show(); host.UpdateLayout();
                var size = control.FindControl<Slider>("SliderFloatingSize")!;
                Assert.Equal(40, size.Value);
                Assert.True(control.FindControl<Grid>("FloatingSizeRow")!.IsVisible);
                var row = control.FindControl<Grid>("FloatingSizeRow")!;
                Assert.Equal(36, row.Height);
                Assert.Equal(3, row.ColumnDefinitions.Count);
                Assert.Equal(120, row.ColumnDefinitions[0].MinWidth);
                Assert.Equal(56, row.ColumnDefinitions[2].MinWidth);
                Assert.Equal(1, Grid.GetColumn(size));
                Assert.Equal(new Thickness(14, 0), size.Margin);
                Assert.NotNull(ToolTip.GetTip(row));
                var label = Assert.IsType<TextBlock>(row.Children[0]);
                Assert.Equal(13, label.FontSize); Assert.Equal(FontWeight.SemiBold, label.FontWeight);
                var referenceLabel = (TextBlock)((Grid)control.FindControl<Slider>("SliderPerHour")!.Parent!).Children[0];
                Assert.Equal(((ISolidColorBrush)referenceLabel.Foreground!).Color, ((ISolidColorBrush)label.Foreground!).Color);
                var value = control.FindControl<TextBlock>("TxtFloatingSize")!;
                Assert.Equal(2, Grid.GetColumn(value)); Assert.Equal(13, value.FontSize);
                Assert.Equal(FontWeight.Bold, value.FontWeight);
                Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Right, value.HorizontalAlignment);
                Assert.Equal(((ISolidColorBrush)control.FindControl<TextBlock>("TxtPerHour")!.Foreground!).Color, ((ISolidColorBrush)value.Foreground!).Color);
                Assert.NotEqual(((ISolidColorBrush)label.Foreground!).Color, ((ISolidColorBrush)value.Foreground!).Color);
                Assert.Equal(label.Bounds.Center.Y, value.Bounds.Center.Y, 1);
                Assert.Equal(36, row.Bounds.Height);
                size.Value = 55;
                Assert.Equal(55, settings.MandatoryVideoFloatingSizePercent);
                Assert.Equal("55%", control.FindControl<TextBlock>("TxtFloatingSize")!.Text);
                control.FindControl<ComboBox>("CmbAppearance")!.SelectedIndex = 0;
                Assert.False(control.FindControl<Grid>("FloatingSizeRow")!.IsVisible);
            }
            finally
            {
                host?.Close();
                settings.MandatoryVideoFloating = savedFloating;
                settings.MandatoryVideoFloatingSizePercent = savedSize;
            }
            return Task.CompletedTask;
        });
    }
}
