using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
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
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var resources = Application.Current!.Resources;
        bool hadLocal = resources.TryGetValue("FxGlowColor", out var previous);
        // P02: Accent reads App.Mods first; a test that ran StartMods earlier would pin it to the default mod.
        var modsProp = typeof(AvApp).GetProperty("Mods", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousMods = modsProp.GetValue(null);
        modsProp.SetValue(null, null);
        Window? host = null;
        try
        {
            // A red mod's -59 degree HSV partner is magenta; the old literal stayed violet.
            resources["FxGlowColor"] = Colors.Red;
            var view = new ExclusivesTabView();
            // Shown so OnLoaded subscribes to CoreMods.ModChanged, the real mod-switch path.
            host = new Window { Content = view, Width = 1200, Height = 900 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var shelf = view.FindControl<ItemsControl>("ExclusivesShelf")!;
            var redRows = shelf.Items.OfType<ExclusiveCardRow>().Where(r => r.Tier == 0 && !r.FreeToday).ToArray();
            Assert.NotEmpty(redRows);
            Assert.All(redRows, r => Assert.Equal(Color.FromArgb(0x4D, 255, 0, 251), ((SolidColorBrush)r.EdgeBrush).Color));
            var badge = (LinearGradientBrush)view.FindControl<Border>("SpotBadge")!.Background!;
            Assert.Equal(Colors.Red, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(255, 0, 251), badge.GradientStops[1].Color);
            AssertSpotChrome(view, Colors.Red, Color.FromRgb(255, 0, 251));
            AssertShelfChrome(redRows, Colors.Red, Color.FromRgb(255, 0, 251));

            // A mod switch (CoreMods.ModChanged -> OnModChanged) must repaint existing shelf slots.
            resources["FxGlowColor"] = Color.FromRgb(0, 255, 0);
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            var greenRows = shelf.Items.OfType<ExclusiveCardRow>().Where(r => r.Tier == 0 && !r.FreeToday).ToArray();
            Assert.NotEmpty(greenRows);
            Assert.All(greenRows, r => Assert.Equal(Color.FromArgb(0x4D, 251, 255, 0), ((SolidColorBrush)r.EdgeBrush).Color));
            Assert.Equal(Color.FromRgb(0, 255, 0), badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(251, 255, 0), badge.GradientStops[1].Color);
            AssertSpotChrome(view, Color.FromRgb(0, 255, 0), Color.FromRgb(251, 255, 0));
            AssertShelfChrome(greenRows, Color.FromRgb(0, 255, 0), Color.FromRgb(251, 255, 0));
            Assert.NotSame(redRows[0], greenRows[0]);
        }
        finally
        {
            host?.Close();
            modsProp.SetValue(null, previousMods);
            if (hadLocal) resources["FxGlowColor"] = previous!;
            else resources.Remove("FxGlowColor");
        }
        return Task.CompletedTask;
    });

    private static Color A(byte alpha, Color c) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>WPF RetintVaultChrome: title shadow, NEW badge, unlock pill (authored alphas kept), 0x66 accent rim.</summary>
    private static void AssertSpotChrome(ExclusivesTabView view, Color accent, Color partner)
    {
        var glow = (DropShadowEffect)view.FindControl<TextBlock>("TxtSpotTitle")!.Effect!;
        Assert.Equal(accent, glow.Color);
        var pill = (LinearGradientBrush)view.FindControl<Border>("SpotVeilPill")!.Background!;
        Assert.Equal(A(0xE6, accent), pill.GradientStops[0].Color);
        Assert.Equal(A(0xD9, partner), pill.GradientStops[1].Color);
        // ExclusiveFeature.All[0] is tiered, so the untiered hero rim is asserted on the edge rule itself.
        Assert.Equal(A(0x66, accent), ((SolidColorBrush)ExclusivesTabView.SpotEdge(0, freeToday: false)).Color);
    }

    /// <summary>WPF AccentGradient(0xFF,0xFF) badge, AccentGradient(0xE6,0xD9) pill, AccentTitleShadow on every shelf card.</summary>
    private static void AssertShelfChrome(ExclusiveCardRow[] rows, Color accent, Color partner)
    {
        foreach (var r in rows)
        {
            var badge = (LinearGradientBrush)r.BadgeFill;
            Assert.Equal(A(0xFF, accent), badge.GradientStops[0].Color);
            Assert.Equal(A(0xFF, partner), badge.GradientStops[1].Color);
            var pill = (LinearGradientBrush)r.PillFill;
            Assert.Equal(A(0xE6, accent), pill.GradientStops[0].Color);
            Assert.Equal(A(0xD9, partner), pill.GradientStops[1].Color);
            Assert.Equal(accent, ((DropShadowEffect)r.TitleGlow).Color);
        }
    }
}
