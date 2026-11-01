using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoFloatingSizeTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Floating_options_follow_the_appearance()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.MandatoryVideoFloating = false;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            try
            {
                host.Show();
                var options = card.FindControl<StackPanel>("PanelFloatingOptions")!;
                var appearance = card.FindControl<ComboBox>("CmbAppearance")!;
                Assert.False(options.IsVisible);

                appearance.SelectedItem = appearance.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(service.Current.MandatoryVideoFloating);
                Assert.True(options.IsVisible);

                appearance.SelectedItem = appearance.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, false));
                Dispatcher.UIThread.RunJobs();
                Assert.False(options.IsVisible);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Floating_options_explain_that_attention_checks_do_not_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.MandatoryVideoFloating = true;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            try
            {
                host.Show();
                var options = card.FindControl<StackPanel>("PanelFloatingOptions")!;
                var note = card.FindControl<TextBlock>("TxtFloatingNoChecks")!;
                Assert.Equal(Loc.Get("video_appearance_no_checks_note"), note.Text);
                Assert.True(options.IsVisible);
                Assert.Contains(note, options.Children);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Size_loads_saves_and_applies_to_the_next_floating_window()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var settings = service.Current;
            settings.MandatoryVideoFloating = true;
            settings.MandatoryVideoFloatingSizePercent = 41;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            try
            {
                host.Show();
                var slider = card.FindControl<Slider>("SliderFloatingSize")!;
                var label = card.FindControl<TextBlock>("TxtFloatingSize")!;
                Assert.Equal((15, 60), (slider.Minimum, slider.Maximum));
                Assert.Equal(41, slider.Value);
                Assert.Equal("41%", label.Text);

                var screen = host.Screens.Primary!;
                var first = new Window();
                MandatoryVideoOverlay.PlaceFloating(first, screen, new Random(1));
                var originalWidth = first.Width;
                service.SaveImmediate();
                Assert.False(service.HasPendingSave);

                slider.Value = 55.6;
                Assert.Equal(56, settings.MandatoryVideoFloatingSizePercent);
                Assert.Equal("56%", label.Text);
                Assert.True(service.HasPendingSave);
                Assert.Equal(originalWidth, first.Width);

                var next = new Window();
                MandatoryVideoOverlay.PlaceFloating(next, screen, new Random(1));
                var area = screen.WorkingArea;
                var expected = MandatoryVideoPresentation.FloatingRect((int)(area.Width / screen.Scaling),
                    (int)(area.Height / screen.Scaling), new Random(1), 56);
                Assert.Equal((double)expected.W, next.Width);
                Assert.NotEqual(originalWidth, next.Width);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }
}
