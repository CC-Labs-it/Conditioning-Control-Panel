using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BrainDrainFeatureControl.xaml:220-230 + .xaml.cs:105,326-335: the "Keep pictures clear"
/// row shows BrainDrainKeepPicturesClear on load and writes it back when toggled.</summary>
public sealed class BrainDrainKeepClearRowTests
{
    [Fact]
    public Task RowLoadsAndWritesTheSetting() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var s = CoreSettings.Current;
        var was = s.BrainDrainKeepPicturesClear;
        Window? host = null;
        try
        {
            s.BrainDrainKeepPicturesClear = true;
            var card = new BrainDrainFeatureControl();
            host = new Window { Content = card };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var chk = card.FindControl<CheckBox>("ChkKeepClear")!;

            Assert.True(chk.IsChecked);
            chk.IsChecked = false;
            Assert.False(s.BrainDrainKeepPicturesClear);
            chk.IsChecked = true;
            Assert.True(s.BrainDrainKeepPicturesClear);
        }
        finally
        {
            host?.Close();
            s.BrainDrainKeepPicturesClear = was;
        }
        return Task.CompletedTask;
    });
}
