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

public sealed class VideoReplaySwitchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_toggle_loads_saves_and_follows_external_changes(bool initial)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service; // the assembly hook restores this and the isolated profile
            var s = CoreSettings.Current;
            var saved = s.MandatoryVideoReplayAfterFailedCheck;
            s.MandatoryVideoReplayAfterFailedCheck = initial;
            var card = new VideoFeatureControl();
            var window = new Window { Content = card };
            try
            {
                window.Show();
                var toggle = card.FindControl<CheckBox>("ChkReplayFailedCheck")!;
                Assert.Equal(initial, toggle.IsChecked);
                service.SaveImmediate();
                Assert.False(service.HasPendingSave);
                toggle.IsChecked = !initial;
                Assert.Equal(!initial, s.MandatoryVideoReplayAfterFailedCheck);
                Assert.True(service.HasPendingSave);
                s.MandatoryVideoReplayAfterFailedCheck = initial;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(initial, toggle.IsChecked);
            }
            finally
            {
                window.Close();
                s.MandatoryVideoReplayAfterFailedCheck = saved;
            }
            return Task.CompletedTask;
        });
    }
}
