using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary><see cref="SkillPurchase"/> against WPF SkillTreeService.PurchaseSkillAsync + ProfileSyncService.PurchaseSkillAsync
/// (ProfileSyncService.cs:3980) on a fake server: no network, sandbox achievements.json.</summary>
public sealed class SkillPurchaseTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-skill-buy-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Server(params string[] bodies) : HttpMessageHandler
    {
        public readonly List<JObject> Requests = new();
        public string? Path;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Path = r.RequestUri!.AbsolutePath;
            Requests.Add(JObject.Parse(await r.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies[Requests.Count - 1]) };
        }
    }

    private static AppSettings Settings() => new()
    {
        UnifiedId = "u1", SkillPoints = 50, UnlockedSkills = new List<string> { "pink_hours" },
        SeasonStatsSeason = "2026-10", SeasonPointsSpent = 0, StreakShieldsRemaining = 0,
    };

    [Fact]
    public async Task SuccessAdoptsServerValuesCountsPrestigeAndAppliesTheSkill()
    {
        var s = Settings();
        var achievements = new AchievementEngine(new AchievementStore(Path.Combine(_dir, "achievements.json")));
        var server = new Server("{\"success\":true,\"skill_points\":45,\"unlocked_skills\":[\"good_girl_streak\"],\"lifetime_points_spent\":150}");
        int saves = 0;

        var (ok, error) = await SkillPurchase.PurchaseAsync("good_girl_streak", s, new V2AuthService(() => s, server), achievements,
            () => throw new InvalidOperationException("no sync on success"), () => saves++);

        Assert.True(ok, error);
        Assert.Equal("/v2/user/purchase-skill", server.Path);
        var body = Assert.Single(server.Requests);
        Assert.Equal(("u1", "good_girl_streak", 50), ((string)body["unified_id"]!, (string)body["skill_id"]!, (int)body["skill_points"]!));
        Assert.Equal(45, s.SkillPoints);                                   // the server's balance
        Assert.Equal(new[] { "pink_hours", "good_girl_streak" }, s.UnlockedSkills);   // unioned, none lost
        Assert.Equal(1, s.StreakShieldsRemaining);                         // ApplySkillEffects
        Assert.Equal(5, s.SeasonPointsSpent);                              // the season's spend bucket
        Assert.Equal(150, achievements.Progress.LifetimeSkillPointsSpent); // 5 tracked, then the server total is ahead
        Assert.True(achievements.Progress.IsUnlocked("window_shopping"));
        Assert.True(saves >= 1);
    }

    [Fact]
    public async Task BalanceRefusalSyncsOnceAndAsksAgainThenAdoptsWhatSurvives()
    {
        var s = Settings();
        s.SkillPoints = 6;
        var refusal = "{\"success\":false,\"error\":\"Not enough points\",\"skill_points\":3}";
        var server = new Server(refusal, "{\"success\":true,\"skill_points\":1,\"unlocked_skills\":[\"good_girl_streak\"]}");
        int syncs = 0;
        var (ok, _) = await SkillPurchase.PurchaseAsync("good_girl_streak", s, new V2AuthService(() => s, server), null,
            () => { syncs++; return Task.FromResult(true); }, () => { });
        Assert.True(ok);
        Assert.Equal((1, 2, 1), (syncs, server.Requests.Count, s.SkillPoints));

        // A refusal that survives the sync lowers the wallet to the server's number.
        s = Settings();
        s.SkillPoints = 6;
        server = new Server(refusal, refusal);
        var (ok2, error2) = await SkillPurchase.PurchaseAsync("good_girl_streak", s, new V2AuthService(() => s, server), null,
            () => Task.FromResult(true), () => { });
        Assert.False(ok2);
        Assert.Equal("Not enough points", error2);
        Assert.Equal(3, s.SkillPoints);
        Assert.DoesNotContain("good_girl_streak", s.UnlockedSkills);

        // Not signed in: nothing is sent.
        s = Settings();
        s.UnifiedId = null;
        server = new Server();
        Assert.False((await SkillPurchase.PurchaseAsync("good_girl_streak", s, new V2AuthService(() => s, server), null,
            () => Task.FromResult(true), () => { })).Success);
        Assert.Empty(server.Requests);
    }
}
