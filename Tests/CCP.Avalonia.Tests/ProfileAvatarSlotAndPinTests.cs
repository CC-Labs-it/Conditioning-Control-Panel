using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.ProfileCosmetics: the preset avatar's share of the picture slot (#847), the
/// header bust, and click-to-pin with its cap notice.</summary>
public sealed class ProfileAvatarSlotAndPinTests
{
    [Fact]
    public Task OwnPresetTakesAnEmptySlot_NeverAPendingOne_AndYieldsToARealPicture() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, MainShellWindow.FetchProfilePhoto);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        settings.Current.ProfileCosmetics = new ProfileCosmetics { AvatarId = "avatar_bambi_1" };
        var preset = ModArt.AvatarPreset("avatar_bambi_1");
        var photo = ModArt.AvatarPreset("avatar_bambi_2");
        Assert.NotNull(preset);
        Assert.NotNull(photo);
        var fetch = new TaskCompletionSource<Bitmap?>();
        MainShellWindow.FetchProfilePhoto = _ => fetch.Task;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var page = shell.ProfilePage!;
            var avatar = page.ProfileHeroAvatar;
            page.DisplayOwnProfile();   // no Discord picture: the empty slot is final, so the preset takes it
            Assert.Same(preset, avatar.AvatarImage);

            shell.RefreshProfileBubble();   // the header bust wears the same preset
            var bubble = Assert.IsType<ImageBrush>(shell.FindControl<Ellipse>("ProfileBubbleFill")!.Fill);
            Assert.Same(preset, bubble.Source);

            shell.LoadProfilePicture("https://cdn.example/me.png");
            shell.ApplyOwnProfileWardrobe();
            Assert.Null(avatar.AvatarImage);   // #847: a picture is on its way, the preset keeps out
            fetch.SetResult(photo);
            await Until(() => ReferenceEquals(avatar.AvatarImage, photo));
            shell.ApplyOwnProfileWardrobe();
            Assert.Same(photo, avatar.AvatarImage);   // a real picture always wins

            var stale = new TaskCompletionSource<Bitmap?>();
            MainShellWindow.FetchProfilePhoto = _ => stale.Task;
            shell.LoadProfilePicture("https://cdn.example/stale.png");
            page.DisplayOwnProfile();
            stale.SetResult(photo);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Same(preset, avatar.AvatarImage);

            MainShellWindow.FetchProfilePhoto = _ => Task.FromResult<Bitmap?>(null);
            shell.LoadProfilePicture("https://cdn.example/gone.png");
            await Until(() => avatar.AvatarImage != null);
            Assert.Same(preset, avatar.AvatarImage);   // the load came back empty: the slot is free again
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); (CoreSettings.ServiceProvider, MainShellWindow.FetchProfilePhoto) = old; }
    });

    [Fact]
    public Task ViewedCardKeepsItsPresetOutWhileTheLookupPictureLoads() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient, MainShellWindow.FetchProfilePhoto);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        LeaderboardTabView.NewClient = () => new LeaderboardClient(new LookupWire());
        var photo = ModArt.AvatarPreset("avatar_bambi_2");
        var fetch = new TaskCompletionSource<Bitmap?>();
        string? fetched = null;
        MainShellWindow.FetchProfilePhoto = url => { fetched = url; return fetch.Task; };
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var page = shell.ProfilePage!;
            settings.Current.OfflineMode = false;
            await page.OpenProfileAsync("Nyx");   // search -> board row -> GET /user/lookup
            settings.Current.OfflineMode = true;
            await Until(() => fetched != null);
            Assert.Equal("https://cdn.example/nyx.png", fetched);
            Assert.Null(page.ProfileHeroAvatar.AvatarImage);   // their preset is equipped, but the picture is pending
            fetch.SetResult(photo);
            await Until(() => page.ProfileHeroAvatar.AvatarImage != null);
            Assert.Same(photo, page.ProfileHeroAvatar.AvatarImage);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient, MainShellWindow.FetchProfilePhoto) = old;
        }
    });

    [Fact]
    public Task ClickingATileTogglesItsPin_TheFifthFlashesTheCapNotice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var achievementsProp = typeof(global::ConditioningControlPanel.Avalonia.App)
            .GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var oldAchievements = achievementsProp.GetValue(null);
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = true;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-pins-").FullName;
        var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir, "achievements.json")));
        var ids = Achievement.All.Values.Where(a => !a.IsExclusive).Select(a => a.Id).Take(6).ToList();
        foreach (var id in ids.Take(5)) engine.Progress.UnlockedAchievements.Add(id);
        achievementsProp.SetValue(null, engine);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var page = shell.ProfilePage!;
            page.DisplayOwnProfile();
            var click = typeof(DiscordTabView).GetMethod("ProfileAchievementTile_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Click(string id) => click.Invoke(page, new object?[] { new Border { DataContext = new ProfileAchievementTile(id, id) }, null });
            List<string> Pinned() => CoreSettings.Current.ProfileCosmetics?.PinnedAchievements ?? new();

            foreach (var id in ids.Take(4)) Click(id);
            Assert.Equal(ids.Take(4), Pinned());

            var summary = page.FindControl<TextBlock>("TxtProfileUnlockSummary")!;
            var original = (summary.Text, summary.Foreground);
            Click(ids[4]);   // at the cap: refused, and the summary says why
            Assert.Equal(4, Pinned().Count);
            Assert.Equal(Loc.GetF("profile_customize_pins_full", ProfileCosmetics.MaxPinnedAchievements), summary.Text);
            Assert.Equal(Color.Parse("#FF5C7A"), ((ISolidColorBrush)summary.Foreground!).Color);
            Assert.Equal(TimeSpan.FromSeconds(2.5), shell._pinCapNoticeTimer!.Interval);
            Assert.True(shell._pinCapNoticeTimer.IsEnabled);

            await Until(() => !shell._pinCapNoticeTimer.IsEnabled);
            Assert.False(shell._pinCapNoticeTimer.IsEnabled);
            Assert.Equal(original.Text, summary.Text);
            Assert.Same(original.Foreground, summary.Foreground);

            var tile0 = new Border { DataContext = new ProfileAchievementTile(ids[0], ids[0]) };
            foreach (var button in new[] { global::Avalonia.Input.MouseButton.Right, global::Avalonia.Input.MouseButton.Middle })
                click.Invoke(page, new object?[] { tile0, new global::Avalonia.Input.PointerReleasedEventArgs(tile0,
                    new global::Avalonia.Input.Pointer(1, global::Avalonia.Input.PointerType.Mouse, true), shell, default, 0,
                    new global::Avalonia.Input.PointerPointProperties(), global::Avalonia.Input.KeyModifiers.None, button) });
            Assert.Equal(ids.Take(4), Pinned());   // WPF MouseLeftButtonUp: only a left click pins

            Click(ids[0]);   // a second click unpins
            Assert.Equal(ids.Skip(1).Take(3), Pinned());
            Click(ids[5]);   // locked: not pinnable
            Assert.Equal(3, Pinned().Count);
            shell.SetProfileViewingSelf(false);   // someone else's card: the gesture is yours only
            Click(ids[4]);
            Assert.Equal(3, Pinned().Count);
        }
        finally
        {
            shell._pinCapNoticeTimer?.Stop();
            shell.Close(); Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = old;
            achievementsProp.SetValue(null, oldAchievements);
        }
    });

    [Fact]
    public Task ModSwitchRepaintsViewedPins_AndUnsubscribesOnClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, CoreMods.MakeModAwareProvider);
        var settingsBefore = CoreSettings.Current;
        var valuesBefore = (settingsBefore.OfflineMode, settingsBefore.ProfileCosmetics);
        var settings = new SettingsService();
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.OfflineMode = true;
            settings.Current.ProfileCosmetics = new ProfileCosmetics();
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("discord");
            shell.SetProfileViewingSelf(false);
            shell.ApplyViewedProfileWardrobe(new ProfileCosmetics { PinnedAchievements = new() { "plastic_initiation" } });
            var pins = shell.ProfilePage!.FindControl<ItemsControl>("ProfilePinnedShowcase")!;
            var before = pins.ItemsSource;
            CoreMods.MakeModAwareProvider = text => "switched: " + text;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
            await Until(() => !ReferenceEquals(before, pins.ItemsSource));
            var tile = Assert.Single(pins.ItemsSource!.Cast<ProfileAchievementTile>());
            Assert.Equal("plastic_initiation", tile.Id);
            Assert.StartsWith("switched: ", tile.Name);
            Assert.NotNull(tile.Image);
            shell.Close();
            before = pins.ItemsSource;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(before, pins.ItemsSource);
        }
        finally
        {
            try { shell?.Close(); Dispatcher.UIThread.RunJobs(); }
            finally { (CoreSettings.ServiceProvider, CoreMods.MakeModAwareProvider) = old; }
        }
        // Check before the assembly hook runs, so it cannot hide missing local cleanup.
        Assert.Same(old.ServiceProvider, CoreSettings.ServiceProvider);
        Assert.Same(old.MakeModAwareProvider, CoreMods.MakeModAwareProvider);
        Assert.Equal(valuesBefore, (settingsBefore.OfflineMode, settingsBefore.ProfileCosmetics));
    });

    [Fact]
    public Task FailedLookupFreesOwnPresetSlot() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient);
        var settingsBefore = CoreSettings.Current;
        var valuesBefore = (settingsBefore.OfflineMode, settingsBefore.UserDisplayName, settingsBefore.ProfileCosmetics);
        var settings = new SettingsService();
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.OfflineMode = true;
            settings.Current.UserDisplayName = "Nyx";
            settings.Current.ProfileCosmetics = new ProfileCosmetics { AvatarId = "avatar_bambi_1" };
            LeaderboardTabView.NewClient = () => new LeaderboardClient(new LookupWire { FailLookup = true });
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("discord");
            CoreSettings.Current.OfflineMode = false;
            await shell.ProfilePage!.OpenProfileAsync("Nyx");
            CoreSettings.Current.OfflineMode = true;
            await Until(() => shell.ProfilePage.ProfileHeroAvatar.AvatarImage != null);
            Assert.Same(ModArt.AvatarPreset("avatar_bambi_1"), shell.ProfilePage.ProfileHeroAvatar.AvatarImage);
        }
        finally
        {
            try { shell?.Close(); Dispatcher.UIThread.RunJobs(); }
            finally { (CoreSettings.ServiceProvider, LeaderboardTabView.NewClient) = old; }
        }
        Assert.Same(old.ServiceProvider, CoreSettings.ServiceProvider);
        Assert.Same(old.NewClient, LeaderboardTabView.NewClient);
        Assert.Equal(valuesBefore, (settingsBefore.OfflineMode, settingsBefore.UserDisplayName, settingsBefore.ProfileCosmetics));
    });

    private static async Task Until(Func<bool> done)
    {
        for (var end = DateTime.UtcNow.AddSeconds(10); !done() && DateTime.UtcNow < end;)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    /// <summary>A one-trainer board and that trainer's lookup: a picture URL and an equipped preset.</summary>
    private sealed class LookupWire : HttpMessageHandler
    {
        public bool FailLookup;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath switch
            {
                "/v3/leaderboard" => """{ "entries": [ { "unified_id": "u3", "display_name": "Nyx", "level": 29 } ] }""",
                "/user/lookup" when !FailLookup => """
                    { "display_name": "Nyx", "level": 29, "avatar_url": "https://cdn.example/nyx.png",
                      "cosmetics": { "avatar_id": "avatar_bambi_1" } }
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
