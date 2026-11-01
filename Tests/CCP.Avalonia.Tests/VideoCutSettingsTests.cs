using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VideoCutSettingsTests
{
    [Fact]
    public Task Slice_start_is_comfort_and_stays_out_of_the_session_dose_sweep()
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = s.VideoMaxDurationSeconds;
        try
        {
            foreach (var cap in new[] { 30, 0 })
            {
                s.VideoMaxDurationSeconds = cap;
                // Construct the real card without layout, so the classification test needs no fonts.
                var card = new VideoFeatureControl();
                var toggle = card.FindControl<CheckBox>("ChkVideoCutRandomStart")!;
                Assert.False(SessionLock.GetOwned(toggle));
                Assert.DoesNotContain(toggle, SessionLock.FindOwnedControls(card));
                Assert.Equal(cap > 0, toggle.IsEnabled);
                // Dose controls still belong to the sweep; removing the whole sweep is not a fix.
                Assert.Contains(card.FindControl<Slider>("SliderVideoMaxDur")!, SessionLock.FindOwnedControls(card));
            }
        }
        finally { s.VideoMaxDurationSeconds = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Slice_switch_loads_saves_and_tracks_the_cap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.VideoMaxDurationSeconds, s.VideoCutRandomStart);
            Window? host = null;
            try
            {
                s.VideoMaxDurationSeconds = 0;
                s.VideoCutRandomStart = true;
                var card = new VideoFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var toggle = card.FindControl<CheckBox>("ChkVideoCutRandomStart")!;
                Assert.True(toggle.IsChecked);
                Assert.False(toggle.IsEnabled);
                s.VideoMaxDurationSeconds = 30;
                Dispatcher.UIThread.RunJobs();
                Assert.True(toggle.IsEnabled);
                toggle.IsChecked = false;
                Assert.False(s.VideoCutRandomStart);
                s.VideoCutRandomStart = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(toggle.IsChecked);
            }
            finally
            {
                try { host?.Close(); }
                finally { (s.VideoMaxDurationSeconds, s.VideoCutRandomStart) = saved; }
            }
            return Task.CompletedTask;
        });
    }
}
