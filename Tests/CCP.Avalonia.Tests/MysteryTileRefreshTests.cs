using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MysteryTileRefreshTests
{
    [Fact]
    public Task RevealTracksTheGiftAndPremiumState() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var dash = shell.SettingsPage!;
            shell.RefreshMysteryTile("takeover", false);
            Assert.Equal(Loc.Get("mosaic_free_today"), dash.CardMystery.Title);
            Assert.Null(dash.CardMystery.TierBadge);
            Assert.Equal(Loc.Get("tab_takeover"), dash.MysteryRevealName.Text);
            Assert.Equal(1, dash.MysteryRevealBadge.Tier);
            Assert.True(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("fyp", false);
            Assert.Equal(Loc.Get("tab_fyp"), dash.MysteryRevealName.Text);
            shell.RefreshMysteryTile("fyp", true);
            Assert.Equal(0, dash.MysteryRevealBadge.Tier);
            Assert.False(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("unknown", false);
            Assert.Equal(Loc.Get("mosaic_daily_gift"), dash.MysteryRevealName.Text);
            Assert.Equal(0, dash.MysteryRevealBadge.Tier);
            Assert.False(dash.MysteryRevealBadge.FreeToday);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
