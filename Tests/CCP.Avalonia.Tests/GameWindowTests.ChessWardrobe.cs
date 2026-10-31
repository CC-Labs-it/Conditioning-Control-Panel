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
using ConditioningControlPanel.Services.PieceByPiece.Wardrobe;
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
        internal bool Block;
        internal bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            Assert.Equal("test-token", request.Headers.GetValues(PbpNetRelay.TokenHeader).Single());
            Assert.Single(request.Headers.GetValues(PbpNetRelay.VersionHeader));
            if (Block)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
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
            Assert.Equal("owned", (string?)frames[2]["type"]);
            Assert.Equal(2, frames[2]["grants"]!.Count());
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
            PrizeOwnership.IsGranted = _ => false;
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
            w.HandleMessage("{\"type\":\"pbp:look\",\"look\":{}}");
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
