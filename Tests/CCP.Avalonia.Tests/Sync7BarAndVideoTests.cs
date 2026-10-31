using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
            AppBuilder.Configure<AvApp>().UseSkia()
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
            CoreSettings.Current.MandatoryVideoFloating = true;
            CoreSettings.Current.MandatoryVideoFloatingSizePercent = 40;
            var control = new VideoFeatureControl();
            var size = control.FindControl<Slider>("SliderFloatingSize")!;
            Assert.Equal(40, size.Value);
            Assert.True(control.FindControl<StackPanel>("FloatingSizeRow")!.IsVisible);
            size.Value = 55;
            Assert.Equal(55, CoreSettings.Current.MandatoryVideoFloatingSizePercent);
            Assert.Equal("55%", control.FindControl<TextBlock>("TxtFloatingSize")!.Text);
            control.FindControl<ComboBox>("CmbAppearance")!.SelectedIndex = 0;
            Assert.False(control.FindControl<StackPanel>("FloatingSizeRow")!.IsVisible);
            return Task.CompletedTask;
        });
    }
}
