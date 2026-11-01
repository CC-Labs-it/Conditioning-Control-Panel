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
                s.UnifiedId = "other";
                Assert.Empty(PrizeOwnership.Grants());
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
