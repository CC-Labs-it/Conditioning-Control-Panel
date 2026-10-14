using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesModAccentTests
{
    [Fact]
    public Task DesktopVaultReadsTheActiveModsGlowSlot() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var settingsFile = Path.Combine(CorePaths.UserData, "settings.json");
        byte[]? previousFile = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
        using var modsSnapshot = new CoreModsSnapshot();
        var previousProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        ModManifest? manifest = null;
        ModFxPalette? previousPalette = null;
        try
        {
            AvApp.StartMods();
            manifest = AvApp.Mods!.ActiveMod.Manifest;
            previousPalette = manifest.FxPalette;
            manifest.FxPalette = new ModFxPalette { GlowColor = "#0000FF" };
            var view = new ExclusivesTabView();
            var rows = view.FindControl<ItemsControl>("ExclusivesShelf")!.Items
                .OfType<ExclusiveCardRow>().Where(r => r.Tier == 0 && !r.FreeToday).ToArray();
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.Equal(Color.FromArgb(0x4D, 0, 251, 255), ((SolidColorBrush)r.EdgeBrush).Color));
            var badge = (LinearGradientBrush)view.FindControl<Border>("SpotBadge")!.Background!;
            Assert.Equal(Colors.Blue, badge.GradientStops[0].Color);
        }
        finally
        {
            if (manifest != null) manifest.FxPalette = previousPalette;
            settings.SaveImmediate(suppressCloudBackup: true);
            settings.SealForReset();
            CoreSettings.ServiceProvider = previousProvider;
            AvApp.ResetReleaseContent();
            if (previousFile is null) File.Delete(settingsFile);
            else File.WriteAllBytes(settingsFile, previousFile);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefreshRepaintsUntieredRimsFromTheCurrentModAccent() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var resources = Application.Current!.Resources;
        bool hadLocal = resources.TryGetValue("FxGlowColor", out var previous);
        try
        {
            // A red mod's -59 degree HSV partner is magenta; the old literal stayed violet.
            resources["FxGlowColor"] = Colors.Red;
            var view = new ExclusivesTabView();
            var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
            var redRows = shelf.Items.OfType<ExclusiveCardRow>().Where(r => r.Tier == 0 && !r.FreeToday).ToArray();
            Assert.NotEmpty(redRows);
            Assert.All(redRows, r => Assert.Equal(Color.FromArgb(0x4D, 255, 0, 251), ((SolidColorBrush)r.EdgeBrush).Color));
            var badge = (LinearGradientBrush)view.FindControl<Border>("SpotBadge")!.Background!;
            Assert.Equal(Colors.Red, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(255, 0, 251), badge.GradientStops[1].Color);

            // RefreshVault is the tab-show / ModChanged path: a green mod must repaint existing shelf slots.
            resources["FxGlowColor"] = Color.FromRgb(0, 255, 0);
            view.RefreshVault();
            var greenRows = shelf.Items.OfType<ExclusiveCardRow>().Where(r => r.Tier == 0 && !r.FreeToday).ToArray();
            Assert.NotEmpty(greenRows);
            Assert.All(greenRows, r => Assert.Equal(Color.FromArgb(0x4D, 251, 255, 0), ((SolidColorBrush)r.EdgeBrush).Color));
            Assert.Equal(Color.FromRgb(0, 255, 0), badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(251, 255, 0), badge.GradientStops[1].Color);
            Assert.NotSame(redRows[0], greenRows[0]);
        }
        finally
        {
            if (hadLocal) resources["FxGlowColor"] = previous!;
            else resources.Remove("FxGlowColor");
        }
        return Task.CompletedTask;
    });
}
