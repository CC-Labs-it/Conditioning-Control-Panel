using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Web XP claims follow WPF SyncProfileAsync:1970-1985 and ProgressionService.AddClaimedXP.
/// Same fake wire and statics as <see cref="AccountSeedTests"/>.
/// </summary>
public sealed partial class AccountSeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullWebXpStillAcknowledgesMigrationAndReconcilesServerXp(bool queued) => WithFreshInstall(async () =>
    {
        CoreDispatch.PostProvider = null;
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = "previous-claim";
        sync.MarkLoaded(null);
        var before = ProfileAdopt.TotalXp(s);
        var serverXp = (int)XpCurve.CumulativeXpToReachLevel(50, ProfileAdopt.Epoch(s)) + 250;
        wire.SyncReply = new JObject
        {
            ["success"] = true,
            ["user"] = new JObject { ["level"] = 50, ["xp"] = serverXp, ["current_season"] = "2026-08" },
            ["web_xp"] = JValue.CreateNull(),
            ["descent_migration"] = new JObject { ["completed"] = true }
        }.ToString();
        var queue = new Queue<System.Action>();
        if (queued) CoreDispatch.PostProvider = queue.Enqueue;
        Assert.True(await sync.PushAsync("null web XP"));
        if (queued)
        {
            Assert.False(s.DescentMigrationCompleted);
            Assert.Equal(before, ProfileAdopt.TotalXp(s));
            Assert.Single(queue);
        }
        while (queue.TryDequeue(out var action)) action();
        Assert.True(s.DescentMigrationCompleted);
        Assert.Equal(50, s.PlayerLevel);
        Assert.Equal(serverXp, ProfileAdopt.TotalXp(s));
        Assert.Equal("previous-claim", s.LastWebXpClaimId);
    });

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("\"scalar\"")]
    [InlineData("[]")]
    public void NonObjectUserDoesNotBlockIndependentSyncFields(string userJson) => WithFreshInstall(async () =>
    {
        CoreDispatch.PostProvider = null;
        var (wire, sync, _) = SignIn("u1", null);
        FreshTree();
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        sync.MarkLoaded(null);
        var before = ProfileAdopt.TotalXp(s);
        wire.SyncReply = new JObject
        {
            ["success"] = true,
            ["user"] = JToken.Parse(userJson),
            ["skill_points"] = 7,
            ["unlocked_skills"] = new JArray("server_skill"),
            ["cosmetics"] = new JObject { ["banner_id"] = "bambi_neon_den" },
            ["web_xp"] = new JObject { ["claim"] = new JObject { ["id"] = "x", ["amount"] = 1 } },
            ["descent_migration"] = new JObject { ["completed"] = true }
        }.ToString();
        Assert.True(await sync.PushAsync("non-object user"));
        Assert.Equal("x", s.LastWebXpClaimId);
        Assert.Equal(before + 1, ProfileAdopt.TotalXp(s));
        Assert.Equal(7, s.SkillPoints);
        Assert.Contains("server_skill", s.UnlockedSkills);
        Assert.Equal("bambi_neon_den", s.ProfileCosmetics.BannerId);
        Assert.True(s.DescentMigrationCompleted);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerAheadAndWebClaimPayBeforeReconcilingOnTheSameUiTurn(bool queued) => WithFreshInstall(async () =>
    {
        CoreDispatch.PostProvider = null;
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        var before = ProfileAdopt.TotalXp(s);
        var serverXp = (int)XpCurve.CumulativeXpToReachLevel(50, ProfileAdopt.Epoch(s)) + 250;
        wire.SyncReply = new JObject { ["user"] = new JObject { ["level"] = 50, ["xp"] = serverXp,
            ["current_season"] = "2026-08" }, ["web_xp"] = new JObject {
            ["claim"] = new JObject { ["id"] = "ordered", ["amount"] = 100 } } }.ToString();
        var queue = new Queue<System.Action>();
        if (queued) CoreDispatch.PostProvider = queue.Enqueue;
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("combined claim"));
        if (queued) { Assert.Equal(before, ProfileAdopt.TotalXp(s)); Assert.Null(s.LastWebXpClaimId); }
        while (queue.TryDequeue(out var action)) action();
        // WPF ProfileSyncService.cs:1970-1985 pays 100 first; :2390 then adopts the higher server total.
        Assert.Equal(serverXp, ProfileAdopt.TotalXp(s));
        Assert.Equal("ordered", s.LastWebXpClaimId);
        sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("repeat claim"));
        while (queue.TryDequeue(out var action)) action();
        Assert.Equal(serverXp, ProfileAdopt.TotalXp(s));
        Assert.Equal("ordered", (string?)wire.Syncs.Last()["web_xp_claim_ack"]);
        Assert.Equal(serverXp, (int)wire.Syncs.Last()["xp"]!);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedReconciliationCannotCrossAccountOrSession(bool sameAccount) => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        CoreSettings.Current.LastWebXpClaimId = null;
        sync.MarkLoaded(null);
        wire.SyncReply = """{"user":{"level":50,"xp":100000},"web_xp":{"claim":{"id":"stale","amount":100}}}""";
        var queue = new Queue<System.Action>();
        CoreDispatch.PostProvider = queue.Enqueue;
        var before = ProfileAdopt.TotalXp(CoreSettings.Current);
        Assert.True(await sync.PushAsync("queued response"));
        if (sameAccount) { sync.Reset(); sync.MarkLoaded(null); }
        else CoreSettings.Current.UnifiedId = "u2";
        while (queue.TryDequeue(out var action)) action();
        Assert.Equal(before, ProfileAdopt.TotalXp(CoreSettings.Current));
        Assert.Null(CoreSettings.Current.LastWebXpClaimId);
    });

    [Fact]
    public void MalformedQueuedXpResponseDoesNotEscapeTheUiOperation() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        sync.MarkLoaded(null);
        wire.SyncReply = """{"user":{"level":"invalid","xp":100}}""";
        var queue = new Queue<System.Action>();
        CoreDispatch.PostProvider = queue.Enqueue;
        var before = ProfileAdopt.TotalXp(CoreSettings.Current);
        Assert.True(await sync.PushAsync("malformed response"));
        Assert.Null(Record.Exception(Assert.Single(queue)));
        Assert.Equal(before, ProfileAdopt.TotalXp(CoreSettings.Current));
    });

}
