using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class OwnershipBadgeTests
{
    [Fact]
    public Task Grants_and_revokes_repaint_cards_both_rack_states_and_studio_chips() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var savedId = CoreAccount.UnifiedUserId;
        var favorites = CoreSettings.Current.RailFavorites.ToList();
        var recent = CoreSettings.Current.RailRecent.ToList();
        MainShellWindow? shell = null;
        try
        {
            CoreAccount.UnifiedUserId = "badge-test";
            PrizeOwnership.ResetHeld();
            CoreSettings.Current.RailFavorites.Clear();
            CoreSettings.Current.RailFavorites.Add("door.studio");
            CoreSettings.Current.RailFavorites.Add("door.home");
            CoreSettings.Current.RailRecent.Clear();
            CoreSettings.Current.RailRecent.Add("tab.studio");
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("studio");
            var dash = shell.Named<SettingsTabView>("SettingsTab")!;
            var rack = shell.StudioRack!;
            var flash = dash.FindControl<Control>("CardFlash")!;
            var bubbles = dash.FindControl<Control>("CardBubblePop")!;
            Border Badge(Control c) => Assert.Single(c.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "V2Badge"));
            var revision = 0;
            void Apply(params string[] grants)
            {
                PrizeOwnership.ApplySnapshot("badge-test", ++revision, grants);
                Dispatcher.UIThread.RunJobs();
            }
            void Check(bool f, bool b)
            {
                Assert.Equal(f, Badge(flash).IsVisible);
                Assert.Equal(b, Badge(bubbles).IsVisible);
                foreach (var key in new[] { "flash", "bubbles" })
                {
                    var row = rack.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Tag as string == key);
                    var pills = row.GetVisualDescendants().OfType<Border>().Where(x => x.Name == "V2Badge").ToArray();
                    Assert.Equal(2, pills.Length); // resting strip and checked tile
                    var tile = row.GetVisualDescendants().OfType<Grid>().Single(g => g.Children.OfType<Border>().Any(p => p.Name == "V2Badge") && g.Children.OfType<TextBlock>().Any(t => t.FontSize == 13));
                    var label = tile.Children.OfType<TextBlock>().Single(t => t.FontSize == 13);
                    Assert.Equal(key == "flash" ? f : b, label.Margin.Right > 26);
                    Assert.All(pills, pill => Assert.Equal(key == "flash" ? f : b, pill.IsVisible));
                }
                var chips = dash.FindControl<StackPanel>("FavoritesList")!.Children.OfType<Button>();
                var chipPills = chips.SelectMany(c => Assert.IsType<Grid>(c.Content).Children.OfType<Border>()).Where(x => x.Name == "V2Badge");
                Assert.Equal(f || b ? 1 : 0, chipPills.Count());
                var recentChip = Assert.Single(dash.FindControl<StackPanel>("RecentList")!.Children.OfType<Button>());
                Assert.Equal(f || b ? 1 : 0, Assert.IsType<Grid>(recentChip.Content).Children.OfType<Border>().Count(x => x.Name == "V2Badge")); // drawer may be collapsed: read content, not visuals
            }
            Apply(); Check(false, false);
            foreach (var grant in new[] { PrizeGrants.FlashDriftBounce, PrizeGrants.FlashPendulum, PrizeGrants.JackpotRemix, PrizeGrants.BubbleRain, PrizeGrants.BubbleSpiralIn })
            {
                Apply(grant);
                var isFlash = grant != PrizeGrants.BubbleRain && grant != PrizeGrants.BubbleSpiralIn;
                Check(isFlash, !isFlash);
                Apply(); Check(false, false);
            }
            // Reattachment must read grants missed while detached.
            var settingsHost = new Window();
            var detached = new SettingsTabView();
            try
            {
                settingsHost.Content = detached;
                settingsHost.Show();
                settingsHost.Content = null;
                Apply(PrizeGrants.BubbleRain);
                Assert.False(Badge(detached.FindControl<Control>("CardBubblePop")!).IsVisible);
                settingsHost.Content = detached;
                Assert.True(Badge(detached.FindControl<Control>("CardBubblePop")!).IsVisible);
            }
            finally { settingsHost.Close(); }
            return Task.CompletedTask;
        }
        finally
        {
            shell?.Close();
            PrizeOwnership.ResetHeld();
            CoreAccount.UnifiedUserId = savedId;
            CoreSettings.Current.RailFavorites.Clear();
            CoreSettings.Current.RailFavorites.AddRange(favorites);
            CoreSettings.Current.RailRecent.Clear();
            CoreSettings.Current.RailRecent.AddRange(recent);
        }
    });
}
