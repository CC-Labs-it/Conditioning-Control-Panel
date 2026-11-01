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
            Assert.Equal(JTokenType.Null, logout["accountId"]!.Type);
            Assert.Null(logout["grants"]);
            Assert.Null(PrizeOwnership.Grants());
            sent.Clear();
            CoreAccount.UnifiedUserId = "other";
            Dispatcher.UIThread.RunJobs();
            var switchFrame = Assert.Single(sent);
            Assert.Equal("other", (string?)switchFrame["accountId"]);
            Assert.Null(switchFrame["grants"]);
            CoreAccount.UnifiedUserId = "discord:12";
            Assert.Null(PrizeOwnership.Grants());
        }
        finally { PrizeOwnership.ResetHeld(); (s.UnifiedId, s.OfflineMode) = (id, offline); }
    });

    private sealed class DelayedRaceReply : System.Net.Http.HttpMessageHandler
    {
        internal readonly System.Threading.Tasks.TaskCompletionSource<System.Net.Http.HttpResponseMessage> Reply = new();
        internal readonly System.Threading.Tasks.TaskCompletionSource Started = new();
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        { Started.TrySetResult(); return Reply.Task; }
    }

    [Fact]
    public void Glassroad_OldProfileReply_IsRejectedAfterAccountSwitchEvenBackToSameAccount() => RunGlassroad("1", (_, _, _) =>
    {
        var oldHttp = GameWindow.PbpHttpClient;
        var handler = new DelayedRaceReply();
        using var http = new System.Net.Http.HttpClient(handler);
        try
        {
            GameWindow.PbpHttpClient = () => http;
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            sent.Clear();
            w.HandleMessage("{\"type\":\"net\",\"id\":\"old\",\"method\":\"GET\",\"path\":\"/v2/race/profile\"}");
            Assert.True(handler.Started.Task.Wait(TimeSpan.FromSeconds(10)));
            CoreAccount.UnifiedUserId = "other";
            CoreAccount.UnifiedUserId = "discord:12";
            handler.Reply.SetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent("{\"grants\":[\"cos.kart.body.petal\"]}") });
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            var reply = sent.Single(x => (string?)x["id"] == "old");
            Assert.Equal(0, (int)reply["status"]!);
            Assert.Equal("", (string?)reply["body"]);
        }
        finally { GameWindow.PbpHttpClient = oldHttp; }
    });

    [Fact]
    public void Glassroad_DelayedSnapshot_CannotReplaceNewAccount()
    {
        var s = CoreSettings.Current;
        var (oldId, oldOffline, oldAccount) = (s.UnifiedId, s.OfflineMode, CoreAccount.UnifiedUserId);
        var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        using var entered = new System.Threading.ManualResetEventSlim();
        using var resume = new System.Threading.ManualResetEventSlim();
        System.Threading.Tasks.Task? late = null;
        try
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode, CoreAccount.UnifiedUserId) = ("a", "test", false, "a");
            PrizeOwnership.ResetHeld();
            PrizeOwnership.BeforeSnapshotLock = () => { entered.Set(); resume.Wait(); };
            late = System.Threading.Tasks.Task.Run(() => PrizeOwnership.ApplySnapshot("a", 99, new[] { "cos.kart.body.a" }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            PrizeOwnership.BeforeSnapshotLock = null;
            (s.UnifiedId, CoreAccount.UnifiedUserId) = ("b", "b");
            PrizeOwnership.ApplySnapshot("b", 1, new[] { "cos.kart.body.b" });
            resume.Set();
            Assert.True(late.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal(new[] { "cos.kart.body.b" }, PrizeOwnership.Grants());
        }
        finally
        {
            resume.Set(); late?.Wait(TimeSpan.FromSeconds(10));
            PrizeOwnership.BeforeSnapshotLock = null;
            PrizeOwnership.ResetHeld();
            CoreAccount.UnifiedUserId = oldAccount;
            (s.UnifiedId, s.OfflineMode) = (oldId, oldOffline);
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
        }
    }

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
