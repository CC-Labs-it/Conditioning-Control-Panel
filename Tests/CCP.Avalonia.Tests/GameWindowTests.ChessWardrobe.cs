using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    private sealed class EquipHttp : HttpMessageHandler
    {
        internal readonly List<(string Method, string Path, string Body)> Calls = new();
        internal string Reply = "{}";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string[] Grants = Array.Empty<string>();
        internal bool BlockOwnership;
        internal bool Block;
        internal bool Cancelled;
        internal Action? Delivered;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            Assert.Equal("test-token", request.Headers.GetValues(PbpNetRelay.TokenHeader).Single());
            var ownership = request.RequestUri.AbsolutePath == "/v2/shop/state";
            if (!ownership) Assert.Single(request.Headers.GetValues(PbpNetRelay.VersionHeader));
            if (Block || (ownership && BlockOwnership))
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            if (ownership) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject
                { ["ok"] = true, ["prizes"] = new JObject { ["revision"] = 1, ["grants"] = new JArray(Grants) } }.ToString()) };
            Delivered?.Invoke();
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }
    private static void FinishEquip(Task task)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(task.IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
    }
    [Fact]
    public void ChessWardrobe_StartupReadsGrants_AndIdentityChangesClearThem() => RunBoard(() =>
    {
        var old = (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider);
        using var transport = new EquipHttp { Grants = new[] { "cos.chess.set.royal-capes" } };
        using var http = new HttpClient(transport);
        try
        {
            PrizeOwnership.ResetHeld();
            GameWindow.PbpHttpClient = () => http;
            GameWindow.PbpServerBase = () => "http://equip.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;
            CoreSettings.Current.OfflineMode = false;
            CoreSettings.Current.UnifiedId = CoreAccount.UnifiedUserId = "first";
            CoreSettings.Current.ChessLook = PbpLookRules.Write(new ChessLook("chess.set.royal-capes", null, null), 0);
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            w.PbpOsAnimations = () => Task.FromResult(true);
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            FinishEquip(w.PbpOwnershipPending);
            Assert.True(PrizeOwnership.IsGranted("cos.chess.set.royal-capes"));
            Assert.Equal("chess.set.royal-capes", (string?)frames.Last(f => (string?)f["type"] == "pbp:look")["look"]!["set"]);
            Assert.Contains(transport.Calls, c => c.Method == "GET" && c.Path == "/v2/shop/state?unified_id=first");
            transport.Grants = Array.Empty<string>();
            CoreSettings.Current.UnifiedId = CoreAccount.UnifiedUserId = "second";
            Dispatcher.UIThread.RunJobs();
            FinishEquip(w.PbpOwnershipPending);
            Assert.False(PrizeOwnership.IsGranted("cos.chess.set.royal-capes"));
            Assert.Empty(frames.Last(f => (string?)f["type"] == "owned")["grants"]!);
            Assert.Equal(JTokenType.Null, frames.Last(f => (string?)f["type"] == "pbp:look")["look"]!["set"]!.Type);
            transport.BlockOwnership = true;
            CoreSettings.Current.UnifiedId = CoreAccount.UnifiedUserId = "third";
            Dispatcher.UIThread.RunJobs();
            var pending = w.PbpOwnershipPending;
            CoreAccount.UnifiedUserId = CoreSettings.Current.UnifiedId = null;
            Dispatcher.UIThread.RunJobs();
            FinishEquip(pending);
            Assert.True(transport.Cancelled);
            Assert.Empty(frames.Last(f => (string?)f["type"] == "owned")["grants"]!);
            w.Close();
            var observers = (Delegate?)typeof(CoreAccount).GetField("UnifiedIdentityChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            Assert.DoesNotContain(observers?.GetInvocationList() ?? Array.Empty<Delegate>(), d => ReferenceEquals(d.Target, w));
            var count = (transport.Calls.Count, frames.Count);
            CoreSettings.Current.UnifiedId = CoreAccount.UnifiedUserId = "closed";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count, (transport.Calls.Count, frames.Count));
        }
        finally { GameWindow.CloseAllForPanic(); PrizeOwnership.ResetHeld(); (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider) = old; }
    });
    [Theory]
    [InlineData(200, "{bad")]
    [InlineData(404, "{}")]
    [InlineData(200, "{\"equip\":{\"chess\":{\"banner\":\"prime.banner.arena\"},\"updatedAt\":200}}")]
    public void ChessWardrobe_LocalFirst_FailuresKeepLocal_AndNewerServerWins(int status, string reply) => RunBoard(() =>
    {
        var old = (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, PrizeOwnership.IsGranted, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider);
        using var transport = new EquipHttp { Status = (HttpStatusCode)status, Reply = reply };
        using var http = new HttpClient(transport);
        var local = new ChessLook("chess.set.royal-capes", null, null);
        try
        {
            PrizeOwnership.ResetHeld();
            GameWindow.PbpHttpClient = () => http;
            GameWindow.PbpServerBase = () => "http://equip.test";
            PrizeOwnership.IsGranted = id => id is "cos.chess.set.royal-capes" or "cos.prime.banner.arena";
            CoreAccount.UnifiedUserId = "test-user";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;
            CoreSettings.Current.UnifiedId = "test-user";
            CoreSettings.Current.OfflineMode = false;
            CoreSettings.Current.ChessLook = PbpLookRules.Write(local, 100);
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            w.PbpOsAnimations = () => Task.FromResult(true);
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            FinishEquip(w.PbpEquipPending);
            Assert.Equal("owned", (string?)frames[3]["type"]);   // after settings, identity and the media state
            Assert.Equal(2, frames[3]["grants"]!.Count());
            var adopted = status == 200 && reply != "{bad";
            Assert.Equal(adopted ? "prime.banner.arena" : null, PbpLookRules.Read(CoreSettings.Current.ChessLook).Look.Banner);
            Assert.Equal(adopted ? null : local.Set, PbpLookRules.Read(CoreSettings.Current.ChessLook).Look.Set);
            Assert.Equal("/v2/equip?unified_id=test-user", transport.Calls[0].Path);
            w.HandleMessage("{\"type\":\"pbp:look\",\"look\":{\"set\":\"chess.set.royal-capes\",\"banner\":\"prime.banner.arena\",\"insignia\":\"chess.insignia.star\"}}");
            FinishEquip(w.PbpEquipPending);
            Assert.Equal(new ChessLook(local.Set, "prime.banner.arena", null), PbpLookRules.Read(CoreSettings.Current.ChessLook).Look);
            var put = JObject.Parse(transport.Calls.Last().Body);
            Assert.Equal("PUT", transport.Calls.Last().Method);
            Assert.Equal("chess", (string?)put["family"]);
            Assert.Equal(JTokenType.Null, put["value"]!["insignia"]!.Type);
            Assert.Equal(status != 200, PbpLookRules.IsOwed(CoreSettings.Current.ChessLook));
            var saved = PbpLookRules.Read(CoreSettings.Current.ChessLook).Look;
            w.Close();
            PrizeOwnership.IsGranted = _ => false; // startup before grants arrive
            transport.Status = HttpStatusCode.OK;
            transport.Reply = new JObject { ["equip"] = new JObject { ["chess"] = new JObject(),
                ["updatedAt"] = status == 404 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100000 : 0 } }.ToString();
            w = GameWindow.Launch(GameWindow.PbpId)!;
            w.PbpOsAnimations = () => Task.FromResult(true);
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            FinishEquip(w.PbpEquipPending);
            Assert.Equal(saved, PbpLookRules.Read(CoreSettings.Current.ChessLook).Look);
            Assert.False(PbpLookRules.IsOwed(CoreSettings.Current.ChessLook));
            Assert.Equal("PUT", transport.Calls.Last().Method);
            Assert.True(JToken.DeepEquals(saved.ToJObject(), JObject.Parse(transport.Calls.Last().Body)["value"]));
            Assert.Equal(JTokenType.Null, frames[^1]["look"]!["set"]!.Type);
            // A successful old delivery must not acknowledge a newer pick, even with the same look.
            transport.Delivered = () => { var (look, at) = PbpLookRules.Read(CoreSettings.Current.ChessLook);
                CoreSettings.Current.ChessLook = PbpLookRules.Write(look, at + 1, owed: true); };
            w.HandleMessage("{\"type\":\"pbp:look\",\"look\":{}}");
            FinishEquip(w.PbpEquipPending);
            Assert.True(PbpLookRules.IsOwed(CoreSettings.Current.ChessLook));
            transport.Delivered = null;
            var beforeChange = frames.Count;
            PrizeOwnership.ApplySnapshot("test-user", 1, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(beforeChange + 2, frames.Count);
            Assert.Empty(frames[^2]["grants"]!);
            Assert.Equal(JTokenType.Null, frames[^1]["look"]!["set"]!.Type);
            var count = transport.Calls.Count;
            w.HandleMessage("{\"type\":\"open-prize-counter\",\"tab\":\"chess\"}");
            Assert.Equal(count, transport.Calls.Count);
            transport.Block = true;
            PrizeOwnership.IsGranted = _ => true;
            w.HandleMessage("{\"type\":\"pbp:look\",\"look\":{\"banner\":\"prime.banner.arena\"}}");
            GameWindow.CloseAllForPanic();
            FinishEquip(w.PbpEquipPending);
            Assert.True(transport.Cancelled);
            var posted = frames.Count;
            PrizeOwnership.ApplySnapshot("test-user", 2, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(posted, frames.Count);
        }
        finally { PrizeOwnership.ResetHeld(); (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, PrizeOwnership.IsGranted, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider) = old; }
    });
}
