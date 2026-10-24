using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Xunit;

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

    [Fact]
    public Task OpenPickerBrushFollowsRuntimeColorChange() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var resources = Application.Current!.Resources;
        var old = Application.Current.FindResource("DarkerBg");
        var w = Picker();
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            resources["DarkerBg"] = Color.Parse("#FF203040");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF203040"), ColorOf(w.Background));
        }
        finally { resources["DarkerBg"] = old; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PickerBoolBindingsCollapseAndRevealRowDetails() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var w = Picker();
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ItemsControl>("ItemList")!;
            foreach (bool show in new[] { false, true, false })
            {
                var row = new AwarenessPickRow { Label = "Editor", Detail = show ? "Detail" : "", IsRecommended = show };
                list.ItemsSource = new[] { row };
                w.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var detail = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.FontSize == 11);
                var chip = list.GetVisualDescendants().OfType<Border>().Single(b => b.Padding == new Thickness(6, 2));
                Assert.Equal(show, detail.IsVisible);
                Assert.Equal(show, chip.IsVisible);
                Assert.Equal(show ? "Detail" : "", detail.Text);
            }
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WorkshopActionKeepsContentHoverAndDisabledChrome() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var cell = new WorkshopCommunityCell();
        var w = new Window { Content = cell, Width = 500, Height = 400 };
        try
        {
            w.Show(); Dispatcher.UIThread.RunJobs();
            var button = cell.FindControl<Button>("BtnBrowsePrompts")!;
            var chrome = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Chrome");
            Assert.Equal(11, button.FontSize);
            Assert.Equal(new CornerRadius(6), chrome.CornerRadius);
            Assert.Equal(Color.Parse("#FF1C1A2E"), ColorOf(chrome.Background));
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), t => !string.IsNullOrEmpty(t.Text));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
            w.MouseMove(point); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#FF2A2440"), ColorOf(chrome.Background));
            button.IsEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.45, button.Opacity);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

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
