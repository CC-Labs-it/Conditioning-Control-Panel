using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VideoGazeLocalizationTests
{
    [Fact]
    public void LabelAndTooltipFollowLanguageChanges() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        var card = new VideoFeatureControl();
        var host = new Window { Content = card };
        try
        {
            host.Show();
            var row = card.FindControl<Grid>("VideoGazeRow")!;
            var label = card.FindControl<TextBlock>("VideoGazeLabel")!;
            foreach (var language in new[] { "en", "fr" })
            {
                loc.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("setting_stare_to_click"), label.Text);
                Assert.Equal(Loc.Get("tooltip_stare_to_click"), ToolTip.GetTip(row));
            }
        }
        finally { host.Close(); loc.SetLanguage(previous); }
    });
}
