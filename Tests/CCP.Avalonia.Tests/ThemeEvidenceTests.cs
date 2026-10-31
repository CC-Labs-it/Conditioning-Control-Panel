using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using BindingFlags = System.Reflection.BindingFlags;

namespace CCP.Avalonia.Tests;

public sealed class ThemeEvidenceTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static AwarenessAppPickerDialog Picker() => new(AwarenessListKind.Deny,
        Array.Empty<string>(), new[] { "editor" });

    // WPF Resources/Theme/Colors.xaml:9-19, Dialogs/AwarenessAppPickerDialog.xaml:7,75.
    [Fact]
    public Task PickerUsesWpfColorTokens() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var w = Picker();
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF121220"), ColorOf(w.Background));
            Assert.Equal(Color.Parse("#FF181830"), ColorOf(w.GetVisualDescendants().OfType<Border>()
                .First(b => b.DataContext is AwarenessPickRow && b.CornerRadius == new CornerRadius(6)).Background));
            Assert.Equal(Color.Parse("#FFA7A7C1"), (Color)Application.Current!.FindResource("TextMuted")!);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>WPF MainWindow/MainWindow.xaml.cs:2512 overwrites the brush entry on a mod
    /// switch. The open picker's DynamicResource must follow that same real mod switch.</summary>
    [Fact]
    public Task ModSwitchRepaintsOpenPickerBackground() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var snapshot = new CoreModsSnapshot();
        var oldMods = AvApp.Mods;
        if (oldMods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var startId = mods.ActiveModId;
        var res = Application.Current!.Resources;
        var saved = res.Keys.ToDictionary(k => k, k => res[k]);
        var refresh = typeof(MainShellWindow).GetMethod("RefreshThemeAwareElements",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var shell = new MainShellWindow();
        var w = Picker();
        try
        {
            shell.Show(); w.Show(); Dispatcher.UIThread.RunJobs();
            var before = ColorOf(w.Background);
            mods.ActivateMod(startId == BuiltInMods.InfectionControlId ? BuiltInMods.CCPDefaultId : BuiltInMods.InfectionControlId);
            refresh.Invoke(shell, null);
            Dispatcher.UIThread.RunJobs();
            var expected = Color.Parse(mods.GetBackgroundColorHex() ?? "#1A1A2E");
            Assert.NotEqual(before, expected);
            Assert.Equal(expected, ColorOf(w.Background));
        }
        finally
        {
            w.Close();
            if (mods.ActiveModId != startId) mods.ActivateMod(startId);
            shell.Close();
            foreach (var k in res.Keys.ToArray()) if (!saved.ContainsKey(k)) res.Remove(k);
            foreach (var (k, v) in saved) res[k] = v;
            typeof(AvApp).GetProperty(nameof(AvApp.Mods), BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldMods);
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    /// <summary>Rows come from the dialog's own build (MakeRow): a group token carries the detail
    /// and, on the deny list only, the recommended chip; a plain app carries neither.
    /// WPF Dialogs/AwarenessAppPickerDialog.xaml:84-94.</summary>
    [Theory]
    [InlineData(AwarenessListKind.Deny)]
    [InlineData(AwarenessListKind.TitleAllow)]
    public Task PickerBoolBindingsCollapseAndRevealRowDetails(AwarenessListKind kind) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var w = new AwarenessAppPickerDialog(kind, Array.Empty<string>(),
            new[] { AwarenessPrivacyRules.GroupPasswordManagers, "editor" });
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ItemsControl>("ItemList")!;
            var rows = list.Items.OfType<AwarenessPickRow>().ToArray();
            Assert.Equal(2, rows.Length);
            foreach (var row in rows)
            {
                bool group = row.Raw == AwarenessPrivacyRules.GroupPasswordManagers;
                var face = list.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.DataContext == row && b.CornerRadius == new CornerRadius(6));
                var detail = face.GetVisualDescendants().OfType<TextBlock>().Single(t => t.FontSize == 11);
                var chip = face.GetVisualDescendants().OfType<Border>().Single(b => b.Padding == new Thickness(6, 2));
                Assert.Equal(group, detail.IsVisible);
                Assert.Equal(group && kind == AwarenessListKind.Deny, chip.IsVisible);
            }
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>WPF Views/Controls/Companion/Runtime/WorkshopCellTheme.xaml:43-65.</summary>
    [Fact]
    public Task WorkshopActionKeepsContentHoverAndDisabledChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var w = new MainShellWindow();
        try
        {
            w.Show();
            // Companion v2 (WPF CompanionTabView.xaml.cs:36-40): the Community cell lives on
            // Companion > Personality (WPF PersonalityPage.xaml.cs:47), not in the collapsed room.
            w.ShowTab("personality");
            Dispatcher.UIThread.RunJobs();
            var personality = w.Named<ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages.PersonalityPage>("CompanionPersonalityTab")!;
            var cell = personality.GetVisualDescendants().OfType<WorkshopCommunityCell>().Single();
            var button = cell.FindControl<Button>("BtnBrowsePrompts")!;
            var chrome = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Chrome");
            Assert.Equal(11, button.FontSize);
            Assert.Equal(new CornerRadius(6), chrome.CornerRadius);
            Assert.Equal(Color.Parse("#FF1C1A2E"), ColorOf(chrome.Background));
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => !string.IsNullOrEmpty(t.Text));
            button.BringIntoView(); Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
            w.MouseMove(point); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF2A2440"), ColorOf(chrome.Background));
            button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.45, button.Opacity);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    // WPF Views/Controls/Companion/Themes/CompanionTheme.xaml:382-395.
    [Fact]
    public Task CompanionRoomUsesClippedShellAndNonInteractiveWash() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var room = new CompanionRoomView();
        var w = new Window { Content = room, Width = 1200, Height = 800 };
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            var shell = Assert.IsType<Border>(room.Content);
            Assert.Equal(new CornerRadius(14), shell.CornerRadius);
            Assert.Equal(new Thickness(1), shell.BorderThickness);
            Assert.True(shell.ClipToBounds);
            var backdrop = Assert.IsType<LinearGradientBrush>(shell.Background);
            Assert.Equal(new RelativePoint(0, 1, RelativeUnit.Relative), backdrop.EndPoint);
            var washes = room.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Theme == room.FindResource("CmpRoomWashStyle")).ToArray();
            Assert.Equal(3, washes.Length);
            Assert.All(washes, b => Assert.False(b.IsHitTestVisible));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    // WPF Resources/Theme/SeasonRecapCard.xaml:20-22,44-49; Services/RecapTheme.cs:17-29.
    [Fact]
    public Task RecapModSwitchRecolorsVisibleRankButKeepsGoldInkAndFoil() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreMods.AccentColorHexProvider;
        var w = new SeasonRecapWindow();
        try
        {
            CoreMods.AccentColorHexProvider = () => "#00C850";
            w.Show(); Dispatcher.UIThread.RunJobs();
            var card = w.GetVisualDescendants().OfType<SeasonRecapCard>().First();
            card.PrepareForStill();
            Assert.Equal(Color.Parse("#FF00C850"), ColorOf(card.FindControl<TextBlock>("StatRank")!.Foreground));
            CoreMods.AccentColorHexProvider = () => "#2040FF";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF2040FF"), ColorOf(card.FindControl<TextBlock>("StatRank")!.Foreground));
            foreach (var (key, hex) in new[] { ("RecapGoldBrush", "#FFFFD166"), ("RecapInkBrush", "#FFF3ECFF"), ("RecapFoilCyanBrush", "#FF34E0E8") })
                Assert.Equal(Color.Parse(hex), ColorOf((IBrush)card.FindResource(key)!));
        }
        finally { CoreMods.AccentColorHexProvider = old; w.Close(); }
        return Task.CompletedTask;
    });
}
