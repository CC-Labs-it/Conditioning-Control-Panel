using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VideoCardFloatingRowsTests
{
    [Fact]
    public async Task Rows_load_save_refresh_and_follow_floating_with_live_motion()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.MandatoryVideoFloating = true;
            s.MandatoryVideoFloatingSpeedPercent = 52;
            s.MandatoryVideoFloatingCount = 3;
            s.MandatoryVideoFloatingTogether = true;
            s.MandatoryVideoFloatingKeepSound = true;
            var card = new VideoFeatureControl();
            var host = new Window { Content = card };
            var floating = new Window { Width = 300, Height = 200 };
            host.Show(); floating.Show();
            try
            {
                var frame = new FloatingVideoFrame(floating);
                var rows = card.FindControl<StackPanel>("FloatingExtraRows")!;
                var speed = card.FindControl<Slider>("SliderFloatingSpeed")!;
                var count = card.FindControl<ComboBox>("CmbFloatingCount")!;
                var together = card.FindControl<CheckBox>("ChkFloatingTogether")!;
                var sound = card.FindControl<CheckBox>("ChkFloatingKeepSound")!;
                var appearance = card.FindControl<ComboBox>("CmbAppearance")!;
                Assert.True(rows.IsVisible);
                Assert.Equal(52, speed.Value);
                Assert.Equal(3, ((ComboBoxItem)count.SelectedItem!).Tag);
                Assert.True(together.IsChecked);
                Assert.True(sound.IsChecked);
                Assert.Equal(0.52, frame.MotionSpeed, 2);
                speed.Value = 120;
                count.SelectedIndex = 0;
                together.IsChecked = false;
                sound.IsChecked = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(100, s.MandatoryVideoFloatingSpeedPercent);
                Assert.Equal(1, s.MandatoryVideoFloatingCount);
                Assert.False(s.MandatoryVideoFloatingTogether);
                Assert.False(s.MandatoryVideoFloatingKeepSound);
                Assert.Equal(1.0, frame.MotionSpeed, 2);
                s.MandatoryVideoFloatingSpeedPercent = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(10, speed.Value);
                s.MandatoryVideoFloatingCount = 999;
                s.MandatoryVideoFloatingTogether = true;
                s.MandatoryVideoFloatingKeepSound = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(10, speed.Value);
                Assert.Equal(4, ((ComboBoxItem)count.SelectedItem!).Tag);
                Assert.True(together.IsChecked);
                Assert.True(sound.IsChecked);
                appearance.SelectedIndex = 0;
                Assert.False(rows.IsVisible);
                appearance.SelectedIndex = 1;
                Assert.True(rows.IsVisible);
            }
            finally { floating.Close(); host.Close(); }
            return Task.CompletedTask;
        });
    }
}
