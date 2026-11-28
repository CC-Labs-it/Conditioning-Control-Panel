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
    private sealed class StockHttp : HttpMessageHandler
    {
        internal string? StockQuery;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Query.Contains("caps="))
            {
                StockQuery = request.RequestUri.Query;
                Assert.Equal("test-token", request.Headers.GetValues(PbpNetRelay.TokenHeader).Single());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"tabs\":[{\"id\":\"chess\",\"later\":[{\"id\":\"chess.banner.crest\",\"from\":\"2026-11-30\"}]}]}") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public void PieceByPiece_ReadyPostsStockDates() => RunBoard(() =>
    {
        var old = (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, CoreSettings.Current.UnifiedId, CoreSettings.Current.OfflineMode);
        using var handler = new StockHttp();
        using var http = new HttpClient(handler);
        try
        {
            GameWindow.PbpHttpClient = () => http;
            GameWindow.PbpServerBase = () => "http://stock.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;
            CoreSettings.Current.OfflineMode = false;
            CoreSettings.Current.UnifiedId = CoreAccount.UnifiedUserId = "stock-user";
            var w = GameWindow.Launch(GameWindow.PbpId)!;
            w.PbpOsAnimations = () => Task.FromResult(true);
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            var end = DateTime.UtcNow.AddSeconds(5);
            while (!frames.Any(f => (string?)f["type"] == "pbp:stock") && DateTime.UtcNow < end)
            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            var stock = Assert.Single(frames.Where(f => (string?)f["type"] == "pbp:stock"));
            Assert.Equal("chess.banner.crest", (string?)stock["later"]![0]!["id"]);
            Assert.Equal("2026-11-30", (string?)stock["later"]![0]!["from"]);
            Assert.Contains("unified_id=stock-user", handler.StockQuery);
            Assert.Contains("caps=chess2", handler.StockQuery);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, CoreSettings.Current.UnifiedId, CoreSettings.Current.OfflineMode) = old;
        }
    });
}
