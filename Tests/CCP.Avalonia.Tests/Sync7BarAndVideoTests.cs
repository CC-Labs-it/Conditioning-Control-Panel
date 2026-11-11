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
