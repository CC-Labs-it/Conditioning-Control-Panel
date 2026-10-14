using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProfileCosmeticsPaintTests
{
    [Fact]
    public Task OwnCardAndSavePaintBannerAccent_EmptySaveClearsBoth() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        settings.Current.ProfileCosmetics = new ProfileCosmetics { BannerId = CosmeticsPool.Banners[0].Id, Accent = "#43B581" };
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var page = shell.ProfilePage!;
            page.DisplayOwnProfile();
            Assert.NotNull(page.FindControl<Border>("ProfileHeroBanner")!.Background);
            Assert.Equal(Color.Parse("#9943B581"), ((ISolidColorBrush)page.ProfileHeroCard.BorderBrush!).Color);
            Assert.Equal(Color.Parse("#43B581"), ((ISolidColorBrush)page.FindControl<TextBlock>("TxtProfileRecordHeader")!.Foreground!).Color);
            shell.PersistOwnCosmetics(new ProfileCosmetics { Accent = "#FFD700" });
            Assert.Null(page.FindControl<Border>("ProfileHeroBanner")!.Background);
            Assert.Equal(Color.Parse("#99FFD700"), ((ISolidColorBrush)page.ProfileHeroCard.BorderBrush!).Color);
            shell.PersistOwnCosmetics(new ProfileCosmetics());
            Assert.Equal(Color.Parse("#99FF69B4"), ((ISolidColorBrush)page.ProfileHeroCard.BorderBrush!).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)page.FindControl<TextBlock>("TxtProfileRecordHeader")!.Foreground!).Color);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ViewedLoadoutPaintsKnownTitlesPins_ClearingStripsItWithoutShowingOwnPlaceholders() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.SetProfileViewingSelf(false);
            var page = shell.ProfilePage!;
            shell.ApplyViewedProfileWardrobe(new ProfileCosmetics
            {
                BannerId = CosmeticsPool.Banners[0].Id, Accent = "#FFD700", TitleId = "plastic_initiation",
                PinnedAchievements = { "plastic_initiation", "missing" }
            });
            var title = page.FindControl<TextBlock>("TxtProfileEquippedTitle")!;
            Assert.True(title.IsVisible);
            Assert.Equal(CoreMods.MakeModAware(Achievement.All["plastic_initiation"].TitleName), title.Text);
            var pins = page.FindControl<ItemsControl>("ProfilePinnedShowcase")!;
            var tile = Assert.Single(pins.ItemsSource!.Cast<ProfileAchievementTile>());
            Assert.Equal("plastic_initiation", tile.Id);
            Assert.NotNull(tile.Image);
            Assert.False(page.FindControl<StackPanel>("ProfilePinnedPlaceholders")!.IsVisible);
            shell.ApplyViewedProfileWardrobe(null);
            Assert.Null(page.FindControl<Border>("ProfileHeroBanner")!.Background);
            Assert.False(title.IsVisible);
            Assert.Equal(string.Empty, title.Text);
            Assert.Null(pins.ItemsSource);
            Assert.False(page.FindControl<StackPanel>("ProfilePinnedPlaceholders")!.IsVisible);
            shell.SetProfileViewingSelf(true);
            shell.ApplyViewedProfileWardrobe(new ProfileCosmetics { BannerId = "missing", Accent = "invalid", TitleId = "missing" });
            Assert.Null(page.FindControl<Border>("ProfileHeroBanner")!.Background);
            Assert.False(title.IsVisible);
            Assert.True(page.FindControl<StackPanel>("ProfilePinnedPlaceholders")!.IsVisible);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
