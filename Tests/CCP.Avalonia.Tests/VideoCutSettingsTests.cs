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
    public async Task Slice_switch_loads_saves_and_tracks_the_cap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            s.VideoMaxDurationSeconds = 0;
            s.VideoCutRandomStart = true;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            host.Show();
            try
            {
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
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }
}
