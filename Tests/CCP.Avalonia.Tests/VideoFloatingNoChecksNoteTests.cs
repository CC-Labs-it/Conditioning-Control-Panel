using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF VideoFeatureControl.xaml:230 (#3980 slice): floating windows show that they skip the
/// attention checks, under the size row, only while Floating is the appearance.</summary>
public sealed class VideoFloatingNoChecksNoteTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task NoChecksNoteFollowsTheFloatingAppearance()
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
                var note = card.FindControl<TextBlock>("TxtFloatingNoChecks")!;
                Assert.Equal(Loc.Get("video_appearance_no_checks_note"), note.Text);
                Assert.True(note.IsVisible);

                var appearance = card.FindControl<ComboBox>("CmbAppearance")!;
                appearance.SelectedItem = appearance.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, false));
                Dispatcher.UIThread.RunJobs();
                Assert.False(service.Current.MandatoryVideoFloating);
                Assert.False(note.IsVisible);

                appearance.SelectedItem = appearance.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(note.IsVisible);
            }
            finally { host.Close(); }
            return Task.CompletedTask;
        });
    }
}
