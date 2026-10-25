using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Read-only cloud (unit 6, part 2): the leaderboard and the profile card lookup, transcribed bodies (field names from
/// WPF LeaderboardService's DTOs, GET /v3/leaderboard and GET /user/lookup) through a fake handler. Same class as the
/// part-1 no-write tests: both touch CoreAccount / CoreSettings statics.
/// </summary>
public sealed partial class AccountSeedTests
{
    private const string Board = """
    { "entries": [
        { "unified_id": "u2", "display_name": "velvet_hush", "level": 32, "xp": 90000, "total_xp_earned": 400000,
          "highest_level_ever": 40, "achievements_count": 12, "is_online": true, "patreon_tier": 2 },
        { "unified_id": "u1", "display_name": "Bambi Prime", "level": 31, "xp": 120000, "total_xp_earned": 300000,
          "highest_level_ever": 31, "achievements_count": 20, "is_season0_og": true, "is_patreon": true },
        { "unified_id": "u7", "display_name": "me", "level": 20, "xp": 50000, "total_xp_earned": 500000,
          "highest_level_ever": 20, "achievements_count": 5 },
        { "unified_id": "u3", "display_name": "Nyx", "level": 29, "xp": 90000, "total_xp_earned": 100000,
          "highest_level_ever": 50, "achievements_count": 9, "discord_id": "33" } ],
      "total_users": 1234, "online_users": 2, "your_rank": 4, "your_total": 1234 }
    """;

    private const string Lookup = """
    { "display_name": "Nyx", "level": 29, "xp": 90000, "total_bubbles_popped": 1208, "total_flashes": 96,
      "total_video_minutes": 75.5, "total_lock_cards_completed": 31, "achievements_count": 2,
      "achievements": ["first_flash", "bubble_100"], "is_online": true, "is_whitelisted": true, "is_staff": true,
      "staff_role": "admin", "patreon_tier": 3, "avatar_url": "https://cdn.example/a.png", "is_season0_og": true }
    """;

    /// <summary>Records every request; answers the board, the lookup, and 500 for anything else.</summary>
    private sealed class BoardWire : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        /// <summary>When set, every answer waits for it (an in-flight fetch).</summary>
        public TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add($"{r.Method} {r.RequestUri!.PathAndQuery}");
            if (Gate != null) await Gate.Task;
            var (code, body) = r.RequestUri.AbsolutePath switch
            {
                "/v3/leaderboard" => (HttpStatusCode.OK, Board),
                "/user/lookup" => (HttpStatusCode.OK, Lookup),
                _ => (HttpStatusCode.InternalServerError, "{}"),
            };
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task LeaderboardAndProfileCard_ReadTranscribedResponses_WithGetsOnly()
    {
        var wire = new BoardWire();
        var client = new LeaderboardClient(wire);

        var (page, error) = await client.FetchAsync<LeaderboardEntryData>("monthly", "u7", new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(error);
        Assert.Equal("GET /v3/leaderboard?season=2026-09&limit=200&unified_id=u7", wire.Seen[0]);
        Assert.Equal((1234, 2, 4, 1234), (page!.TotalUsers, page.OnlineUsers, page.YourRank, page.YourTotal));

        // Monthly: XP, ties by level (MainWindow.Leaderboard.cs RankLeaderboardEntries).
        var monthly = LeaderboardClient.Rank(page.Entries!, allTime: false);
        Assert.Equal(new[] { "u1", "u2", "u3", "u7" }, monthly.Select(e => e.UnifiedId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, monthly.Select(e => e.Rank));
        Assert.Equal(("120.0k", 31, 1, "I"), (monthly[0].XpColumnDisplay, monthly[0].LevelColumnValue, monthly[0].EffectivePatreonTier, monthly[0].PatreonTierRoman));
        // All-time: total XP earned, peak level in the Level column.
        var allTime = LeaderboardClient.Rank(page.Entries!, allTime: true);
        Assert.Equal(new[] { "u7", "u2", "u1", "u3" }, allTime.Select(e => e.UnifiedId));
        Assert.Equal(("500.0k", 20), (allTime[0].XpColumnDisplay, allTime[0].LevelColumnValue));

        await client.FetchAsync<LeaderboardEntryData>("all-time", null, DateTime.UtcNow);
        Assert.Equal("GET /v3/leaderboard?season=all-time&limit=200", wire.Seen[1]);

        // The profile card's read.
        var card = await client.LookupUserAsync("Nyx");
        Assert.Equal("GET /user/lookup?display_name=Nyx", wire.Seen[2]);
        Assert.Equal(("Nyx", 29, 90000, 1208, 96, 75.5, 31), (card!.DisplayName, card.Level, card.Xp, card.BubblesPopped, card.GifsSpawned, card.VideoMinutes, card.LockCardsCompleted));
        Assert.Equal(new[] { "first_flash", "bubble_100" }, card.Achievements);
        Assert.True(card.IsOnline && card.IsWhitelisted && card.IsStaff && card.IsSeason0Og);
        Assert.Equal(("admin", 3, "https://cdn.example/a.png"), (card.StaffRole, card.PatreonTier, card.AvatarUrl));

        // Failures: a non-2xx board is an error string, a failed lookup is null.
        var bad = new LeaderboardClient(new FailWire());
        Assert.Equal("Server returned InternalServerError", (await bad.FetchAsync<LeaderboardEntryData>("monthly", null, DateTime.UtcNow)).error);
        Assert.Null(await bad.LookupUserAsync("x"));

        // Unit 6 is READ-ONLY: the board and the card are GETs, nothing else.
        Assert.All(wire.Seen, r => Assert.True(r.StartsWith("GET "), $"write endpoint called: {r}"));
    }

    private sealed class FailWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") });
    }

    [Fact]
    public async Task LeaderboardTab_RendersTheFetchedBoard_SelfRow_AndOfflineHonestly()
    {
        var s = CoreSettings.Current;
        var (oldOffline, oldClient, oldId) = (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var wire = new BoardWire();
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            CoreAccount.UnifiedUserId = "u7";
            s.OfflineMode = false;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");

                var tab = new LeaderboardTabView();
                T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
                Assert.Empty(wire.Seen);                                    // nothing before the tab is shown
                await tab.SetLeaderboardMode(false);                        // the monthly board (a fresh tab opens on All-Time)

                Assert.Equal(Loc.GetF("lb_online_and_total", 2, 1234), F<TextBlock>("TxtLeaderboardStatus").Text);
                Assert.Equal(new[] { "u2", "u1", "u3" }, ((IEnumerable<LeaderboardRow>)F<ItemsControl>("PodiumHost").ItemsSource!).Select(r => r.UnifiedId));
                var me = Assert.Single(((IEnumerable<object>)F<ListBox>("LstLeaderboard").ItemsSource!).OfType<LeaderboardRow>());
                Assert.True(me.IsCurrentUser);
                Assert.Equal(Loc.GetF("label_your_rank_0_of_1", 4, 1234), F<TextBlock>("TxtYourRank").Text);
                Assert.Equal("4", F<TextBlock>("TxtYouRankNumber").Text);
                Assert.Equal(Loc.GetF("lb_gap_to_next", 40000.ToString("N0"), "Nyx", 3), F<TextBlock>("TxtYouGap").Text);

                // Offline: no request, the board stays empty and the status says it failed (WPF RefreshAsync).
                s.OfflineMode = true;
                var seen = wire.Seen.Count;
                var offline = new LeaderboardTabView();
                await offline.RefreshLeaderboardAsync();
                Assert.Equal(seen, wire.Seen.Count);
                Assert.Equal(Loc.Get("label_failed_to_load"), offline.FindControl<TextBlock>("TxtLeaderboardStatus")!.Text);
                Assert.Empty((IEnumerable<object>)offline.FindControl<ListBox>("LstLeaderboard")!.ItemsSource!);
            });
            Assert.All(wire.Seen, r => Assert.True(r.StartsWith("GET "), $"write endpoint called: {r}"));
        }
        finally
        {
            (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId) = (oldOffline, oldClient, oldId);
        }
    }

    /// <summary>WPF MainWindow.UiUpdates.cs:570-607: the Hive Mind / Popular Girl header pills read the
    /// leaderboard's last page (online users, server-rank percentile); before a page they say Loading.</summary>
    [Fact]
    public async Task HeaderStatPills_ReadTheLastLeaderboardPage()
    {
        var s = CoreSettings.Current;
        var (oldOffline, oldClient, oldId) = (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var oldSkills = s.UnlockedSkills.ToList();
        var (oldMinutes, oldShields) = (s.TotalConditioningMinutes, s.StreakShieldsRemaining);
        var wire = new BoardWire();
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            CoreAccount.UnifiedUserId = "u7";
            s.OfflineMode = false;
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.AddRange(new[] { "hive_mind", "popular_girl" });
            global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.NoteLeaderboardPage(new ConditioningControlPanel.Services.LeaderboardPage<ConditioningControlPanel.Services.LeaderboardEntryData>());
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");
                var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
                try
                {
                    shell.Show();
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var online = shell.Named<TextBlock>("TxtPillOnlineUsers")!;
                    var pct = shell.Named<TextBlock>("TxtPillRankPercentile")!;
                    Assert.True(shell.Named<Border>("PillOnlineUsers")!.IsVisible);
                    Assert.Equal(Loc.Get("label_loading_2"), pct.Text);      // no page yet

                    await shell.Named<LeaderboardTabView>("LeaderboardTab")!.SetLeaderboardMode(false);
                    Assert.Equal("2", online.Text);
                    Assert.Equal("Top 1%", pct.Text);                          // ceil(4 / 1234 * 100) = 1

                    // Each pill shows only with its skill (WPF UiUpdates.cs:538-627).
                    Assert.False(shell.Named<Border>("PillConditioningTime")!.IsVisible);
                    Assert.False(shell.Named<Border>("StreakFirePill")!.IsVisible);
                    s.UnlockedSkills.Remove("hive_mind");
                    s.UnlockedSkills.AddRange(new[] { "pink_hours", "good_girl_streak" });
                    s.TotalConditioningMinutes = 61.5;
                    s.StreakShieldsRemaining = 1;
                    shell.UpdateStatPills();
                    Assert.False(shell.Named<Border>("PillOnlineUsers")!.IsVisible);
                    Assert.True(shell.Named<Border>("PillConditioningTime")!.IsVisible);
                    Assert.Equal("1h 1m 30s", shell.Named<TextBlock>("TxtPillConditioningTime")!.Text);
                    Assert.True(shell.Named<Border>("StreakFirePill")!.IsVisible);
                    Assert.True(shell.Named<Control>("TxtStreakShieldIcon")!.IsVisible);
                    s.StreakShieldsRemaining = 0;
                    shell.UpdateStatPills();
                    Assert.False(shell.Named<Control>("TxtStreakShieldIcon")!.IsVisible);
                }
                finally { shell.Close(); }
            });
        }
        finally
        {
            (s.OfflineMode, LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId) = (oldOffline, oldClient, oldId);
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.AddRange(oldSkills);
            global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.NoteLeaderboardPage(new ConditioningControlPanel.Services.LeaderboardPage<ConditioningControlPanel.Services.LeaderboardEntryData>());
            (s.TotalConditioningMinutes, s.StreakShieldsRemaining) = (oldMinutes, oldShields);
        }
    }

    [Fact]
    public async Task LeaderboardTab_OneFetchInFlight_AndAModeSwitchMidFetchRefetchesTheNewBoard()
    {
        var (oldClient, oldId) = (LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId);
        var oldOffline = CoreSettings.Current.OfflineMode;
        var wire = new BoardWire { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        try
        {
            LeaderboardTabView.NewClient = () => new LeaderboardClient(wire);
            (CoreAccount.UnifiedUserId, CoreSettings.Current.OfflineMode) = ("u7", false);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var tab = new LeaderboardTabView();
                // Seasons are retired (WPF feda9c907): a fresh tab opens on All-Time, titled as such.
                Assert.True(tab.IsAllTimeMode);
                Assert.Equal(Loc.Get("lb_all_time_title"), tab.FindControl<TextBlock>("TxtLeaderboardSeason")!.Text);
                var first = tab.RefreshLeaderboardAsync();        // all-time, held at the gate
                Assert.True(tab.RefreshLeaderboardAsync().IsCompleted); // WPF IsRefreshing: a second press does nothing
                Assert.Single(wire.Seen);
                Assert.Contains("season=all-time", wire.Seen[0]);

                _ = tab.SetLeaderboardMode(false);                // switched mid-fetch; its own refresh is dropped too
                Assert.Single(wire.Seen);
                Assert.Equal(Loc.Get("social_lb_month_title"), tab.FindControl<TextBlock>("TxtLeaderboardSeason")!.Text);
                Assert.Equal(Loc.Get("social_lb_month_sub"), tab.FindControl<TextBlock>("TxtLeaderboardSubtitle")!.Text);
                wire.Gate.SetResult();
                await first;

                Assert.Equal(2, wire.Seen.Count);                 // the all-time slice was dropped, monthly fetched
                Assert.DoesNotContain("season=all-time", wire.Seen[1]); // monthly asks for the calendar month
                var podium = ((IEnumerable<LeaderboardRow>)tab.FindControl<ItemsControl>("PodiumHost")!.ItemsSource!).ToList();
                Assert.Equal(new[] { "u2", "u1", "u3" }, podium.Select(r => r.UnifiedId)); // monthly order, #1 centred
                Assert.All(podium, r => Assert.False(r.IsAllTimeView));
            });
        }
        finally
        {
            (LeaderboardTabView.NewClient, CoreAccount.UnifiedUserId, CoreSettings.Current.OfflineMode) = (oldClient, oldId, oldOffline);
        }
    }
}
