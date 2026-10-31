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

    [Fact]
    public void SubLevelClaimNudgesFollowUpPushWithAckWithoutQuestCredit() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        var xp = ProfileAdopt.TotalXp(s);
        var level = s.PlayerLevel;
        var quests = 0;
        var source = "";
        System.Action<double, string> nudge = (_, origin) => { source = origin; sync.Nudge("xp:" + origin); };
        System.Action<double, string> quest = (_, _) => quests++;
        ProgressionBank.Awarded += nudge;
        ProgressionBank.QuestAwarded += quest;
        try
        {
            sync.UtcNow = () => T0.AddSeconds(31);
            sync.ClaimWebXp(s, JObject.Parse("""{"web_xp":{"claim":{"id":"nudge","amount":1}}}"""));
            for (var i = 0; i < 100 && wire.Syncs.Count() < 2; i++) await System.Threading.Tasks.Task.Delay(50);
            Assert.Equal(2, wire.Syncs.Count());
            Assert.Equal("nudge", (string?)wire.Syncs.Last()["web_xp_claim_ack"]);
            Assert.Equal((int)(xp + 1), (int)wire.Syncs.Last()["xp"]!);
            Assert.Equal(level, s.PlayerLevel);
            Assert.Equal("Other", source);
            Assert.Equal(0, quests);
            ProgressionBank.Add(1, "Other"); // ordinary Other awards still credit quests
            Assert.Equal(1, quests);
        }
        finally { ProgressionBank.Awarded -= nudge; ProgressionBank.QuestAwarded -= quest; }
    });

    [Fact]
    public void AWebXpClaim_IsPaidOnce_AckedOnEveryPush_AndSkippedOnALevelReset() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        wire.SyncReply = """{"success":true,"web_xp":{"pending":0,"total":100,"claim":{"id":"c1","amount":100}}}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Null(wire.Syncs.Last()["web_xp_claim_ack"]);   // nothing applied yet
        var xp = ProfileAdopt.TotalXp(s);
        Assert.Equal("c1", s.LastWebXpClaimId);

        sync.UtcNow = () => T0.AddSeconds(31);                   // the same claim re-offered: acked, never paid twice
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Equal("c1", (string?)wire.Syncs.Last()["web_xp_claim_ack"]);
        Assert.Equal(xp, ProfileAdopt.TotalXp(s));

        wire.SyncReply = """{"success":true,"level_reset":true,"web_xp":{"claim":{"id":"c2","amount":50}}}""";
        sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Equal("c1", s.LastWebXpClaimId);                 // re-offered after the reset instead
    });

    [Fact]
    public void AWebXpClaim_AddsItsAmountWithoutMultipliers() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        var s = CoreSettings.Current;
        s.LastWebXpClaimId = null;
        wire.SyncReply = """{"success":true}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        var before = ProfileAdopt.TotalXp(s);
        sync.ClaimWebXp(s, JObject.Parse("""{"web_xp":{"claim":{"id":"c9","amount":100}}}"""));
        Assert.Equal(before + 100, ProfileAdopt.TotalXp(s));
    });


}
