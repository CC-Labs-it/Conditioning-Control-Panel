using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit; namespace CCP.Avalonia.Tests;
public sealed partial class AccountSeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerAheadAndWebClaimPayBeforeReconcilingOnTheSameUiTurn(bool queued) => WithFreshInstall(async () =>
    {
        CoreDispatch.PostProvider = null; var (wire, sync, _) = SignIn("u1", L40Profile("u1")); Assert.True(await AccountSeed.LoadProfileAsync()); var s = CoreSettings.Current; s.LastWebXpClaimId = null;
        var before = ProfileAdopt.TotalXp(s); var serverXp = (int)XpCurve.CumulativeXpToReachLevel(50, ProfileAdopt.Epoch(s)) + 250;
        wire.SyncReply = new JObject { ["user"] = new JObject { ["level"] = 50, ["xp"] = serverXp,
            ["current_season"] = "2026-08" }, ["web_xp"] = new JObject {
            ["claim"] = new JObject { ["id"] = "ordered", ["amount"] = 100 } } }.ToString();
        var queue = new Queue<System.Action>(); if (queued) CoreDispatch.PostProvider = queue.Enqueue; sync.UtcNow = () => T0.AddSeconds(31); Assert.True(await sync.PushAsync("combined claim"));
        if (queued) { Assert.Equal(before, ProfileAdopt.TotalXp(s)); Assert.Null(s.LastWebXpClaimId); }
        while (queue.TryDequeue(out var action)) action(); Assert.Equal(serverXp, ProfileAdopt.TotalXp(s)); Assert.Equal("ordered", s.LastWebXpClaimId); sync.UtcNow = () => T0.AddSeconds(62);
        Assert.True(await sync.PushAsync("repeat claim")); while (queue.TryDequeue(out var action)) action(); Assert.Equal(serverXp, ProfileAdopt.TotalXp(s));
        Assert.Equal("ordered", (string?)wire.Syncs.Last()["web_xp_claim_ack"]); Assert.Equal(serverXp, (int)wire.Syncs.Last()["xp"]!);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedReconciliationCannotCrossAccountOrSession(bool sameAccount) => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null); CoreSettings.Current.LastWebXpClaimId = null; sync.MarkLoaded(null);
        wire.SyncReply = """{"user":{"level":50,"xp":100000},"web_xp":{"claim":{"id":"stale","amount":100}}}"""; var queue = new Queue<System.Action>(); CoreDispatch.PostProvider = queue.Enqueue;
        var before = ProfileAdopt.TotalXp(CoreSettings.Current); Assert.True(await sync.PushAsync("queued response"));
        if (sameAccount) { sync.Reset(); sync.MarkLoaded(null); }
        else CoreSettings.Current.UnifiedId = "u2"; while (queue.TryDequeue(out var action)) action(); Assert.Equal(before, ProfileAdopt.TotalXp(CoreSettings.Current)); Assert.Null(CoreSettings.Current.LastWebXpClaimId);
    });
    [Fact]
    public void MalformedQueuedXpResponseDoesNotEscapeTheUiOperation() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null); sync.MarkLoaded(null); wire.SyncReply = """{"user":{"level":"invalid","xp":100}}"""; var queue = new Queue<System.Action>(); CoreDispatch.PostProvider = queue.Enqueue;
        var before = ProfileAdopt.TotalXp(CoreSettings.Current); Assert.True(await sync.PushAsync("malformed response")); Assert.Null(Record.Exception(Assert.Single(queue)));
        Assert.Equal(before, ProfileAdopt.TotalXp(CoreSettings.Current));
    });
}
