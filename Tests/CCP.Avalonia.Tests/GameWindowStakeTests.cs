using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PieceByPieceHostService stakes (:388, :650-660) + StakeAppHooks on this head: the board's
/// <c>stake-*</c> frames reach the Core bridge, replies come back as <c>{type:'stake', op, game}</c>, the wallet
/// adopts the balances, a lost time stake books once however many frames see it settled, and panic closes the
/// board while the settle watch still books (the bridge outlives the window, as in WPF).</summary>
public sealed partial class GameWindowTests
{
    /// <summary>The /v2/stakes server, faked: never the real one.</summary>
    private sealed class FakeStakes : HttpMessageHandler
    {
        public readonly List<(string Op, JObject Body, string Token)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var op = req.RequestUri!.AbsolutePath.Split('/').Last();
            var body = JObject.Parse(await req.Content!.ReadAsStringAsync(ct));
            lock (Calls) Calls.Add((op, body, req.Headers.GetValues("X-Auth-Token").Single()));
            var reply = op switch
            {
                "limits" => "{\"ok\":true,\"sp\":40}",
                "offer" => "{\"ok\":true,\"sp\":30}",
                _ => "{\"ok\":true,\"settled\":{\"result\":\"lost\",\"time_s\":900,\"sp_delta\":0,\"sp\":30}}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }

    [Fact]
    public void AppStartupSeedsTheStakeHooks()
    {
        var src = Regex.Replace(File.ReadAllText(Path.Combine(StakeRoot(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"Platform\.FriendsHead\.SeedInvites\([^;]*;\s*Platform\.StakeHead\.Seed\(\);", src);
    }

    [Fact]
    public void PieceByPiece_StakeFrames_RouteReplyAdoptBookOnceAndOutlivePanic() => Run((shell, _) =>
    {
        var (oldTime, oldRow, oldCreate) = (StakeBridge.TimeOkHook, StakeBridge.EnableLossRowHook, StakeSettlement.CreateShared);
        var (oldBridge, oldSecret, oldChaster) = (GameWindow.PbpStakeBridge, CoreSecrets.RetrieveProvider, ChasterHead.Service);
        var fake = new FakeStakes();
        using var http = new HttpClient(fake);
        var books = 0;
        try
        {
            StakeHead.Seed();
            ChasterHead.Service = null;                                   // no Chaster: time is not on offer
            var s = CoreSettings.Current;
            (s.OfflineMode, s.UnifiedId, s.SkillPoints) = (false, "u1", 0);
            s.ChasterPrices = new List<string>();
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;   // P02: never the keyring
            var settlement = new StakeSettlement((row, sec) => { Assert.Equal("pvp_loss", row); books++; return sec; },
                StakeHead.AdoptSp, StakeHead.Account);
            // The settle watch's wait is stepped (P08): open unless a test holds it.
            TaskCompletionSource? hold = null;
            var waits = 0;
            // StakeBridge.ForApp's shape with the faked server; the seeded head hooks stay in the path.
            GameWindow.PbpStakeBridge = (game, post) => new StakeBridge(game, post,
                new StakeApi(http, FriendsHead.Identity, "http://stakes.test"), settlement,
                () => StakeBridge.TimeOkHook(), () => StakeBridge.EnableLossRowHook(),
                settleDelay: (_, _) => { Interlocked.Increment(ref waits); return hold?.Task ?? Task.CompletedTask; });
            MainShellWindow.LeashGateDueProvider = () => false;
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            Dispatcher.UIThread.RunJobs();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl!);
            JObject Frame(string json)
            {
                var n = sent.Count;
                w.Web.OnWebMessage(json);
                Assert.True(GameWindow.PbpStakePending.Wait(TimeSpan.FromSeconds(10)));   // the frame's own completion (P57)
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(n + 1, sent.Count);
                var o = sent.Last();
                Assert.Equal(("stake", "pbp"), ((string?)o["type"], (string?)o["game"]));
                return o;
            }

            var limits = Frame("{\"type\":\"stake-limits\"}");
            Assert.Equal("limits", (string?)limits["op"]);
            Assert.False((bool)limits["time_ok"]!);
            Assert.NotNull(limits["labels"]);
            Assert.Equal(40, s.SkillPoints);                               // a snapshot raises the wallet

            var time = Frame("{\"type\":\"stake-offer\",\"match\":\"m1\",\"kind\":\"time\",\"amount\":900}");
            Assert.Equal(("offer", "no_chaster"), ((string?)time["op"], (string?)time["reason"]));
            Assert.DoesNotContain(fake.Calls, c => c.Op == "offer");
            Assert.Empty(s.ChasterPrices);                                 // refused: no consent taken

            var offer = Frame("{\"type\":\"stake-offer\",\"match\":\"m1\",\"kind\":\"sp\",\"amount\":10}");
            Assert.Equal(("offer", "m1", true), ((string?)offer["op"], (string?)offer["match"], (bool)offer["ok"]!));
            var call = fake.Calls.Single(c => c.Op == "offer");
            Assert.Equal(("pbp", "sp", 10, "u1", "tok"), ((string?)call.Body["game"], (string?)call.Body["kind"],
                (int)call.Body["amount"]!, (string?)call.Body["unified_id"], call.Token));
            Assert.Equal(30, s.SkillPoints);                               // a receipt lowers it

            var state = Frame("{\"type\":\"stake-state\",\"match\":\"m1\"}");
            Assert.Equal(("state", 900), ((string?)state["op"], (int)state["booked_s"]!));
            Assert.Equal(1, books);
            var settled = Frame("{\"type\":\"stake-settle\",\"match\":\"m1\"}");   // the same match again: no second booking
            Assert.Equal(("settled", 0), ((string?)settled["op"], (int)settled["booked_s"]!));
            Assert.Equal(1, books);

            // Panic mid-watch: the board closes, the watch still books (WPF's static bridge), nothing is posted to it.
            hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            w.Web.OnWebMessage("{\"type\":\"stake-settle\",\"match\":\"m2\"}");
            var watch = GameWindow.PbpStakePending;
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(watch.IsCompleted);                               // still waiting when panic landed
            Assert.Equal(1, books);
            Assert.True(Volatile.Read(ref waits) >= 2);                    // m1's watch and m2's both used the stepped wait
            var before = sent.Count;
            hold.SetResult();
            Assert.True(watch.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, books);
            Assert.Equal(before, sent.Count);

            StakeHead.EnableLossRow();                                     // a time pick's consent: the row, once
            StakeHead.EnableLossRow();
            Assert.Equal(new[] { StakeRules.LossRowId }, s.ChasterPrices);
        }
        finally
        {
            (StakeBridge.TimeOkHook, StakeBridge.EnableLossRowHook, StakeSettlement.CreateShared) = (oldTime, oldRow, oldCreate);
            (GameWindow.PbpStakeBridge, CoreSecrets.RetrieveProvider, ChasterHead.Service) = (oldBridge, oldSecret, oldChaster);
        }
    });

    private static string StakeRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
