using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Rows;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class PresetsModLabelTests
{
    [Fact]
    public Task LabelsFollowModAndLanguage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (AvApp.Mods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var originalId = mods.ActiveModId;
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var view = new PresetsTabView();
        var window = new Window { Content = view };
        var labels = new[] {
            ("TxtDetailFlashLabel", "Flash Images", "section_flash_images"),
            ("TxtDetailVideoLabel", "Mandatory Videos", "label_mandatory_videos"),
            ("TxtDetailSubLabel", "Subliminals", "section_subliminals"),
            ("TxtSessionFlashLabel", "Flash Images", "section_flash_images"),
            ("TxtSessionSubLabel", "Subliminals", "section_subliminals"),
            ("TxtFeatureCornerGif", "Corner GIF", "label_corner_gif") };
        try
        {
            loc.SetLanguage("en");
            mods.ActivateMod(BuiltInMods.CCPDefaultId);
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var (name, _, key) in labels)
                Assert.Equal(loc.Get(key), Source(view, name));
            mods.ActivateMod(BuiltInMods.DronificationId); Dispatcher.UIThread.RunJobs();
            foreach (var (name, english, key) in labels)
            {
                var source = Source(view, name);
                Assert.Equal(PresetsTabView.ModLabel(english, loc.Get(key)), source);
                Assert.EndsWith(CoreMods.MakeModAware(english), source!);
                // The corner GIF row keeps its icon; the 7.4.1 detail rows (RowLabel) wear an emblem instead.
                if (view.FindControl<Control>(name) is IconLabel icon) Assert.IsType<IconGlyph>(icon.Children[0]);
                else Assert.True(Assert.IsType<RowLabel>(view.FindControl<Control>(name)).EmblemPart.IsVisible);
            }
            Assert.Contains("Data Injection", Source(view, "TxtDetailFlashLabel")!);
            loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
            foreach (var (name, english, key) in labels)
                Assert.Equal(PresetsTabView.ModLabel(english, loc.Get(key)), Source(view, name));
            mods.ActivateMod(BuiltInMods.LockedId); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("section_flash_images"), Source(view, "TxtDetailFlashLabel"));
            mods.ActivateMod(BuiltInMods.InfectionControlId); Dispatcher.UIThread.RunJobs();
            Assert.Contains("Temptations", Source(view, "TxtDetailFlashLabel")!);
        }
        finally
        {
            window.Close();
            mods.ActivateMod(originalId);
            loc.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    // The text a label was given: IconLabel.Source (corner GIF row) or RowLabel.Text (detail rows).
    private static string? Source(PresetsTabView view, string name) =>
        view.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name) switch
        {
            IconLabel icon => icon.Source,
            RowLabel row => row.Text,
            _ => null,
        };
}
