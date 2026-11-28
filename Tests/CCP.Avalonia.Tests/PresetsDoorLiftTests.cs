using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Rows;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Emblems;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The 7.4.1 Session Door lift on the Avalonia head (WPF 43c014f363, ccp-bugs #1743): presets and
/// sessions each wear their own hue, the session doors live in the Sessions zone and on the session
/// floor, Import sits beside Export and is never gated on a selection, the detail rows are Readable
/// Rows labels, and the two label inks are the RowLabel mix of each zone's hue.
/// </summary>
public sealed class PresetsDoorLiftTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.FindControl<T>(name)!;

    [Fact]
    public Task Presets_and_sessions_stand_apart() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var view = new PresetsTabView();
        var window = new Window { Content = view, Width = 1400, Height = 1000 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var presets = Find<Border>(view, "PresetsZone");
            var sessions = Find<Border>(view, "SessionsZone");
            var studio = (Color)view.FindResource("SectionHueStudio")!;
            var blue = (Color)view.FindResource("SectionHueNotifications")!;
            Assert.NotEqual(studio, blue);
            Assert.Equal(studio, RowHue.GetHue(presets));
            Assert.Equal(blue, RowHue.GetHue(sessions));

            // The session doors moved into the Sessions zone (a page-wide "+ Create New" read as "new preset").
            Assert.Contains(sessions.GetVisualDescendants(), v => v is Button { Name: "BtnSessionHistory" });
            Assert.Contains(sessions.GetVisualDescendants(), v => v is Button { Name: "BtnCreateSession" });
            Assert.DoesNotContain(presets.GetVisualDescendants(), v => v is Button { Name: "BtnCreateSession" });

            // Export session sits with Start; Import sits beside Export preset and needs no selection.
            var floor = Find<StackPanel>(view, "SessionButtonsPanel");
            Assert.Contains(Find<Button>(view, "BtnExportSession"), floor.Children.OfType<Button>());
            var import = Find<Button>(view, "BtnImportPreset");
            Assert.True(import.IsEnabled);
            Assert.False(Find<Button>(view, "BtnExportPreset").IsEnabled);
            Assert.Same(import.Parent, Find<Button>(view, "BtnExportPreset").Parent);

            // Detail rows are RowLabels with emblems, tinted by their scroller's zone hue.
            var flash = Find<RowLabel>(view, "TxtDetailFlashLabel");
            Assert.Equal(EmblemKind.Many, flash.Emblem);
            Assert.Equal(studio, flash.EffectiveHue);
            var sessionFlash = Find<RowLabel>(view, "TxtSessionFlashLabel");
            Assert.Equal(blue, sessionFlash.EffectiveHue);

            // The two inks: 70% TextLight + 30% each zone's hue.
            uint light = view.FindResource("TextLightBrush") is ISolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            var presetInk = Assert.IsAssignableFrom<ISolidColorBrush>(view.Resources["SdPresetInk"]);
            var sessionInk = Assert.IsAssignableFrom<ISolidColorBrush>(view.Resources["SdSessionInk"]);
            Assert.Equal(RowHue.ToColor(EmblemPalette.LabelTint(light, RowHue.ToArgb(studio))), presetInk.Color);
            Assert.Equal(RowHue.ToColor(EmblemPalette.LabelTint(light, RowHue.ToArgb(blue))), sessionInk.Color);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
}
