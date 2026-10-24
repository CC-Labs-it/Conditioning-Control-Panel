using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesAccentTests
{
    [Fact]
    public Task RefreshRetintsDecorWhileGoldAndTierCommerceStayFixed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var resources = Application.Current!.Resources;
        var old = resources["FxGlowColor"];
        var oldMods = AvApp.Mods;
        Window? host = null;
        try
        {
            SetMods(null);   // the headless fallback: no mod service, the accent follows FxGlowColor
            resources["FxGlowColor"] = Colors.Red;
            var view = new ExclusivesTabView();
            host = new Window { Width = 1400, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var rows = Rows(view);
            var tiered = rows.First(r => r.Tier > 0);
            var tierEdge = tiered.EdgeBrush;
            var gold = ((ISolidColorBrush)view.FindControl<Border>("SpotFreeToday")!.BorderBrush!).Color;
            var untiered = rows.First(r => r.Tier == 0 && !r.FreeToday);
            Assert.Equal(Color.FromArgb(0x4D, 255, 0, 251), ((ISolidColorBrush)untiered.EdgeBrush).Color);
            var badge = (LinearGradientBrush)view.FindControl<Border>("SpotBadge")!.Background!;
            Assert.Equal(Colors.Red, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(255, 0, 251), badge.GradientStops[1].Color);

            // The same mounted view must repaint when its mod changes; constructing a second view
            // would miss a stale resource/brush retained on the spotlight.
            resources["FxGlowColor"] = Colors.Blue;
            view.RefreshVault();
            Dispatcher.UIThread.RunJobs();
            rows = Rows(view);
            Assert.Equal(Color.FromArgb(0x4D, 0, 251, 255),
                ((ISolidColorBrush)rows.First(r => r.Tier == 0 && !r.FreeToday).EdgeBrush).Color);
            Assert.Equal(Colors.Blue, badge.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(0, 251, 255), badge.GradientStops[1].Color);
            Assert.Contains(view.GetVisualDescendants().OfType<Border>()
                .Select(b => b.Background).OfType<LinearGradientBrush>(), brush =>
                brush.GradientStops.Count == 2 &&
                brush.GradientStops[0].Color == Color.FromArgb(0xE6, 0, 0, 255) &&
                brush.GradientStops[1].Color == Color.FromArgb(0xD9, 0, 251, 255));
            Assert.Equal(tierEdge, rows.First(r => r.Feature.Key == tiered.Feature.Key).EdgeBrush);
            Assert.Equal(gold, ((ISolidColorBrush)view.FindControl<Border>("SpotFreeToday")!.BorderBrush!).Color);
        }
        finally
        {
            host?.Close();
            SetMods(oldMods);
            if (old is null) resources.Remove("FxGlowColor");
            else resources["FxGlowColor"] = old;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    /// <summary>The shipped path: App.Mods supplies the glow, and a CoreMods.ModChanged repaints the
    /// mounted view (OnModChanged), with no direct RefreshVault call.</summary>
    [Fact]
    public Task ModSwitchRetintsTheMountedVaultFromTheModsGlow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldMods = AvApp.Mods;
        if (oldMods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var startId = mods.ActiveModId;
        Window? host = null;
        try
        {
            var view = new ExclusivesTabView();
            host = new Window { Width = 1400, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var target = startId == BuiltInMods.InfectionControlId ? BuiltInMods.CCPDefaultId : BuiltInMods.InfectionControlId;
            var before = (Color)view.Resources["VaultAccentColor"]!;
            mods.ActivateMod(target);
            Assert.Equal(target, mods.ActiveModId);
            Dispatcher.UIThread.RunJobs();
            var (r, g, b) = mods.GetGlowColorRgb();
            var glow = Color.FromRgb(r, g, b);
            Assert.NotEqual(before, glow);
            Assert.Equal(glow, (Color)view.Resources["VaultAccentColor"]!);
            var p = global::ConditioningControlPanel.Services.UI.VaultColor.ShiftHue(r, g, b,
                global::ConditioningControlPanel.Services.UI.VaultColor.PartnerHueShift);
            Assert.Equal(Color.FromRgb(p.R, p.G, p.B), (Color)view.Resources["VaultPartnerColor"]!);
            // WPF TintShadow / _exclusiveAccentShadows: spotlight and card title shadows wear the accent.
            var shadows = view.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Effect).OfType<DropShadowEffect>().ToArray();
            Assert.True(shadows.Length > 1);
            Assert.All(shadows, s => Assert.Equal(glow, s.Color));
        }
        finally
        {
            host?.Close();
            if (mods.ActiveModId != startId) mods.ActivateMod(startId);
            SetMods(oldMods);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void SetMods(global::ConditioningControlPanel.Services.ModService? mods) =>
        typeof(AvApp).GetProperty(nameof(AvApp.Mods), BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, mods);

    private static ExclusiveCardRow[] Rows(ExclusivesTabView view) =>
        view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.OfType<ExclusiveCardRow>().ToArray();
}
