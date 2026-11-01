using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF GlassroadRules.cs:54-69: bounded, sorted, deduplicated kart grants only.
    [Fact]
    public void Glassroad_KartGrants_FilterSortAndCap()
    {
        var grants = Enumerable.Range(0, 70).Reverse().Select(i => $"cos.kart.body.{i:00}")
            .Concat(new[] { "cos.kart.body.00", "fx.flash", "COS.kart.body.x", "cos.kart." + new string('x', 60) });
        var rows = GlassroadHostRules.KartGrants(grants).Values<string>().ToArray();
        Assert.Equal(64, rows.Length);
        Assert.Equal("cos.kart.body.00", rows[0]);
        Assert.Equal("cos.kart.body.63", rows[^1]);
        Assert.Empty(GlassroadHostRules.KartGrants(null));
        Assert.Equal(new[] { "cos.kart.body.a" }, GlassroadHostRules.KartGrants(
            new[] { "cos.kart.body.a", "fx.flash", "COS.kart.body.x", "cos.kart." + new string('x', 60) }).Values<string>());
    }

    // WPF init uses the held snapshot (GlassroadHostService.cs:170-182). This head cannot yet
    // fetch it at startup: omit an unknown list so the page keeps its remembered account grants.
    [Fact]
    public void Glassroad_UnknownSnapshot_DoesNotReplaceRememberedKarts()
    {
        var unknown = GlassroadHostRules.InitFrame("u", "Name", 2, false, 0.6, 1);
        Assert.Null(unknown["identity"]!["grants"]);
        var knownEmpty = GlassroadHostRules.InitFrame("u", "Name", 2, false, 0.6, 1, Array.Empty<string>());
        Assert.Empty((JArray)knownEmpty["identity"]!["grants"]!);
    }

    [Fact]
    public void Glassroad_FreshWindow_DefersUnknownThenPushesKnownEmpty() => RunGlassroad("1", (_, _, _) =>
    {
        var s = CoreSettings.Current;
        var (id, offline) = (s.UnifiedId, s.OfflineMode);
        try
        {
            s.UnifiedId = "discord:12";
            s.OfflineMode = false;
            PrizeOwnership.ResetHeld();
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostGlassroadInit();
            Assert.Null(Assert.Single(sent)["identity"]!["grants"]);
            sent.Clear();
            PrizeOwnership.ApplySnapshot("discord:12", 1, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            var owned = Assert.Single(sent);
            Assert.Equal("owned", (string?)owned["type"]);
            Assert.Empty((JArray)owned["grants"]!);
        }
        finally { PrizeOwnership.ResetHeld(); (s.UnifiedId, s.OfflineMode) = (id, offline); }
    });

    [Fact]
    public void Glassroad_FirstEmptySnapshot_IsPushedToOpenWindow() => RunGlassroad("1", (_, _, _) =>
    {
        var s = CoreSettings.Current;
        var (id, offline) = (s.UnifiedId, s.OfflineMode);
        try
        {
            s.UnifiedId = "discord:12";
            s.OfflineMode = false;
            PrizeOwnership.ResetHeld();
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            PrizeOwnership.ApplySnapshot("discord:12", 1, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            var owned = Assert.Single(sent);
            Assert.Equal("owned", (string?)owned["type"]);
            Assert.Empty((JArray)owned["grants"]!);
        }
        finally { PrizeOwnership.ResetHeld(); (s.UnifiedId, s.OfflineMode) = (id, offline); }
    });

    // WPF OwnershipService.Grants uses the account ID, not the online transport (OwnershipService.cs:85-88).
    [Fact]
    public void Glassroad_OfflineSnapshot_RemainsAvailable() => RunGlassroad("1", (_, _, _) =>
    {
        var s = CoreSettings.Current;
        var (id, offline) = (s.UnifiedId, s.OfflineMode);
        try
        {
            s.UnifiedId = "discord:12";
            s.OfflineMode = false;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.ApplySnapshot("discord:12", 1, new[] { "cos.kart.body.petal" });
            s.OfflineMode = true;
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostGlassroadInit();
            Assert.Equal(new[] { "cos.kart.body.petal" }, sent.Last()["identity"]!["grants"]!.Values<string>());
        }
        finally { PrizeOwnership.ResetHeld(); (s.UnifiedId, s.OfflineMode) = (id, offline); }
    });

    // WPF ProfileSyncService.cs:660,697 clears on identity changes; GlassroadHostService.cs:189 pushes it.
    [Fact]
    public void Glassroad_IdentityChange_ClearsTheOpenGarageAndOldSnapshot() => RunGlassroad("1", (_, _, _) =>
    {
        var s = CoreSettings.Current;
        var (id, offline) = (s.UnifiedId, s.OfflineMode);
        try
        {
            s.UnifiedId = "discord:12";
            s.OfflineMode = false;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.ApplySnapshot("discord:12", 1, new[] { "cos.kart.body.petal" });
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            CoreAccount.UnifiedUserId = null;
            Dispatcher.UIThread.RunJobs();
            var logout = Assert.Single(sent);
            Assert.Equal("owned", (string?)logout["type"]);
            Assert.Empty((JArray)logout["grants"]!);
            Assert.Null(PrizeOwnership.Grants());
            sent.Clear();
            CoreAccount.UnifiedUserId = "other";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty((JArray)Assert.Single(sent)["grants"]!);
            CoreAccount.UnifiedUserId = "discord:12";
            Assert.Null(PrizeOwnership.Grants());
        }
        finally { PrizeOwnership.ResetHeld(); (s.UnifiedId, s.OfflineMode) = (id, offline); }
    });

    // WPF GlassroadHostService.cs:170-192: init sees the current account; changed snapshots push while open.
    [Fact]
    public void Glassroad_KartGrants_InitAndLiveOwnership()
    {
        RunGlassroad("1", (_, _, _) =>
        {
            var s = CoreSettings.Current;
            var (id, token, offline) = (s.UnifiedId, s.AuthToken, s.OfflineMode);
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = ("discord:12", "test", false);
                PrizeOwnership.ResetHeld();
                PrizeOwnership.ApplySnapshot("discord:12", 2, new[] { "cos.kart.body.z", "fx.flash", "cos.kart.body.a" });
                var w = GameWindow.Launch(GameWindow.GlassroadId)!;
                Dispatcher.UIThread.RunJobs();
                var sent = new List<JObject>();
                w.Posted += json => sent.Add(JObject.Parse(json));
                w.PostGlassroadInit();
                Assert.Equal(new[] { "cos.kart.body.a", "cos.kart.body.z" }, sent.Single()["identity"]!["grants"]!.Values<string>());
                sent.Clear();
                // The unified account is authoritative even if the settings identity is out of step.
                s.UnifiedId = "other";
                Assert.Equal(new[] { "cos.kart.body.a", "cos.kart.body.z", "fx.flash" }, PrizeOwnership.Grants()!.OrderBy(x => x));
                s.UnifiedId = "discord:12";

                PrizeOwnership.ApplySnapshot("other", 9, new[] { "cos.kart.body.other" });
                PrizeOwnership.ApplySnapshot("discord:12", 1, Array.Empty<string>()); // stale
                PrizeOwnership.ApplySnapshot("discord:12", 3, new[] { "fx.flash", "cos.kart.body.a", "cos.kart.body.z" }); // same set
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(sent);
                PrizeOwnership.ApplySnapshot("discord:12", 4, new[] { "cos.kart.body.new", "fx.flash" });
                Dispatcher.UIThread.RunJobs();
                var owned = Assert.Single(sent);
                Assert.Equal("owned", (string?)owned["type"]);
                Assert.Equal(new[] { "cos.kart.body.new" }, owned["grants"]!.Values<string>());
                sent.Clear();
                // A worker can finish as the window closes; its queued UI post must be dropped.
                System.Threading.Tasks.Task.Run(() => PrizeOwnership.ApplySnapshot("discord:12", 5, Array.Empty<string>())).Wait();
                w.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(sent);
                PrizeOwnership.ApplySnapshot("discord:12", 6, new[] { "cos.kart.body.late" });
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(sent);
                CoreAccount.UnifiedUserId = "other";
                Assert.Null(PrizeOwnership.Grants());
                CoreAccount.UnifiedUserId = "discord:12";
                Assert.Null(PrizeOwnership.Grants()); // switching back cannot revive the old snapshot
            }
            finally
            {
                PrizeOwnership.ResetHeld();
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (id, token, offline);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
        });
    }
}
