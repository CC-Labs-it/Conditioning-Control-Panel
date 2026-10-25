using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
            var glow = page.FindControl<Border>("ProfileAccentGlow")!;
            Assert.Equal((1, Color.Parse("#8043B581")), (glow.BoxShadow.Count, glow.BoxShadow[0].Color));
            shell.PersistOwnCosmetics(new ProfileCosmetics { Accent = "#FFD700" });
            Assert.Null(page.FindControl<Border>("ProfileHeroBanner")!.Background);
            Assert.Equal(Color.Parse("#99FFD700"), ((ISolidColorBrush)page.ProfileHeroCard.BorderBrush!).Color);
            shell.PersistOwnCosmetics(new ProfileCosmetics());
            Assert.Equal(Color.Parse("#99FF69B4"), ((ISolidColorBrush)page.ProfileHeroCard.BorderBrush!).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)page.FindControl<TextBlock>("TxtProfileRecordHeader")!.Foreground!).Color);
            Assert.Equal(0, glow.BoxShadow.Count);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ViewedLoadoutPaintsKnownTitlesPins_ClearingStripsItWithoutShowingOwnPlaceholders() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        var wire = new LookupWire();
        LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var page = shell.ProfilePage!;
            // The user path: search -> board row -> GET /user/lookup -> DiscordTabView hands the loadout to the shell.
            settings.Current.OfflineMode = false;
            await page.OpenProfileAsync("Nyx");
            settings.Current.OfflineMode = true;
            var title = page.FindControl<TextBlock>("TxtProfileEquippedTitle")!;
            // The lookup answers off-thread: yield to the dispatcher until the card is painted (no sleeps, P08).
            for (var end = System.DateTime.UtcNow.AddSeconds(10); !title.IsVisible && System.DateTime.UtcNow < end;)
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Contains(wire.Seen, r => r.StartsWith("/user/lookup"));
            var glow = page.FindControl<Border>("ProfileAccentGlow")!.BoxShadow;
            Assert.Equal((1, Color.Parse("#80FFD700")), (glow.Count, glow[0].Color));
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
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient) = old; }
    });

    /// <summary>A one-trainer board and that trainer's /user/lookup, whose loadout carries one unknown pin.</summary>
    private sealed class LookupWire : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add(r.RequestUri!.PathAndQuery);
            var body = r.RequestUri.AbsolutePath switch
            {
                "/v3/leaderboard" => """{ "entries": [ { "unified_id": "u3", "display_name": "Nyx", "level": 29 } ] }""",
                "/user/lookup" => $$"""
                    { "display_name": "Nyx", "level": 29, "cosmetics": { "banner_id": "{{CosmeticsPool.Banners[0].Id}}",
                      "accent": "#FFD700", "title_id": "plastic_initiation", "pinned_achievements": ["plastic_initiation", "missing"] } }
                    """,
                _ => null
            };
            return Task.FromResult(new HttpResponseMessage(body == null ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
                { Content = new StringContent(body ?? "{}") });
        }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
