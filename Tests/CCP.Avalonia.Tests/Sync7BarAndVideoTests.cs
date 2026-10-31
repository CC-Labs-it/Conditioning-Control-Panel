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

    [Theory]
    [InlineData(0, "label_session_bar_elapsed")]
    [InlineData(10, "label_session_bar_left")]
    public async Task SessionBarTracksClockPauseAndHiddenCountdown(int minutes, string template)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
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
            finally { if (runner.IsRunning) runner.Stop(completed: false); shell.Close(); }
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
