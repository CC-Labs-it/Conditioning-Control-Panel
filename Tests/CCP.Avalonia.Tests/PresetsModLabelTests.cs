using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
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
            ("TxtDetailFlashLabel", "⚡ Flash Images", "section_flash_images"),
            ("TxtDetailVideoLabel", "🎬 Mandatory Videos", "label_mandatory_videos"),
            ("TxtDetailSubLabel", "💭 Subliminals", "section_subliminals"),
            ("TxtSessionFlashLabel", "⚡ Flash Images", "section_flash_images"),
            ("TxtSessionSubLabel", "💭 Subliminals", "section_subliminals"),
            ("TxtFeatureCornerGif", "🖼 Corner GIF", "label_corner_gif") };
        try
        {
            loc.SetLanguage("en");
            mods.ActivateMod(BuiltInMods.CCPDefaultId);
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var (name, _, key) in labels)
                Assert.Equal(loc.Get(key), view.FindControl<IconLabel>(name)!.Source);
            mods.ActivateMod(BuiltInMods.DronificationId); Dispatcher.UIThread.RunJobs();
            foreach (var (name, english, _) in labels)
            {
                var label = view.FindControl<IconLabel>(name)!;
                Assert.Equal(CoreMods.MakeModAware(english), label.Source);
                Assert.IsType<IconGlyph>(label.Children[0]);
            }
            Assert.Contains("Data Injection", view.FindControl<IconLabel>("TxtDetailFlashLabel")!.Source);
            loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
            foreach (var (name, english, _) in labels)
                Assert.Equal(CoreMods.MakeModAware(english), view.FindControl<IconLabel>(name)!.Source);
            mods.ActivateMod(BuiltInMods.LockedId); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("section_flash_images"), view.FindControl<IconLabel>("TxtDetailFlashLabel")!.Source);
            mods.ActivateMod(BuiltInMods.InfectionControlId); Dispatcher.UIThread.RunJobs();
            Assert.Contains("Temptations", view.FindControl<IconLabel>("TxtDetailFlashLabel")!.Source);
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
}
