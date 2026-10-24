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
    [Fact]
    public async Task Replay_toggle_loads_saves_and_follows_external_changes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = s.MandatoryVideoReplayAfterFailedCheck;
            s.MandatoryVideoReplayAfterFailedCheck = false;
            var card = new VideoFeatureControl();
            var window = new Window { Content = card };
            try
            {
                window.Show();
                var toggle = card.FindControl<CheckBox>("ChkReplayFailedCheck")!;
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;
                Assert.True(s.MandatoryVideoReplayAfterFailedCheck);
                s.MandatoryVideoReplayAfterFailedCheck = false;
                Dispatcher.UIThread.RunJobs();
                Assert.False(toggle.IsChecked);
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
