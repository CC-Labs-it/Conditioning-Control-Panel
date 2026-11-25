using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private static void RunAnnex(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        Action<GameWindow, List<JObject>> body) => Run((_, launcher) =>
    {
        var s = CoreSettings.Current;
        var old = (GameWindow.ArcademyAnnexSend, s.OfflineMode);
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            GameWindow.ArcademyAnnexSend = send;
            s.OfflineMode = false;
            launcher.Hide(); w.Show(); Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            body(w, frames);
        }
        finally { w.Close(); (GameWindow.ArcademyAnnexSend, s.OfflineMode) = old; }
    });

    private static JToken?[] AnnexReplies(List<JObject> frames) =>
        frames.Where(f => (string?)f["type"] == "annex-stats").Select(f => f["body"]).ToArray();

    private static HttpResponseMessage Reply(HttpStatusCode code, string text) => new(code) { Content = new StringContent(text) };

    // WPF ArcademyHostService.cs:5476-5482: offline mode answers null and never leaves the machine.
    [Fact]
    public void Arcademy_AnnexStatsOfflineRepliesNullWithoutRequest()
    {
        int calls = 0;
        RunAnnex((_, _) => { calls++; return Task.FromResult(Reply(HttpStatusCode.OK, "{}")); }, (w, frames) =>
        {
            CoreSettings.Current.OfflineMode = true;
            w.HandleMessage("{\"type\":\"annex-stats\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(JTokenType.Null, Assert.Single(AnnexReplies(frames))!.Type);
        });
        Assert.Equal(0, calls);
    }

    // WPF ArcademyHostService.cs:5484-5504: GET, no token; non-200, oversized and unparseable bodies are null.
    [Fact]
    public void Arcademy_AnnexStatsRepliesBodyOrNullOncePerAsk()
    {
        var replies = new Queue<HttpResponseMessage>(new[]
        {
            Reply(HttpStatusCode.OK, "{\"students\":42}"), Reply(HttpStatusCode.InternalServerError, "{\"students\":1}"),
            Reply(HttpStatusCode.OK, "{\"pad\":\"" + new string('x', 400_001) + "\"}"), Reply(HttpStatusCode.OK, "<html>"),
        });
        var seen = new List<HttpRequestMessage>();
        RunAnnex((r, _) => { seen.Add(r); return Task.FromResult(replies.Dequeue()); }, (w, frames) =>
        {
            for (int i = 0; i < 4; i++)
            {
                w.HandleMessage("{\"type\":\"annex-stats\"}");
                Settle(w.ArcademyLastAnnex);
            }
            var bodies = AnnexReplies(frames);
            Assert.Equal(4, bodies.Length);
            Assert.Equal(42, (int?)bodies[0]!["students"]);
            Assert.All(bodies.Skip(1), b => Assert.Equal(JTokenType.Null, b!.Type));
        });
        Assert.All(seen, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Equal("https://codebambi-proxy.vercel.app/v2/arcademy/annex/stats", r.RequestUri!.ToString());
            Assert.Null(r.Headers.Authorization);
        });
    }

    // Panic cancels the open ask (one null reply); a window torn down mid-flight is a stale generation and hears nothing.
    [Fact]
    public void Arcademy_AnnexStatsPanicAndStaleGeneration()
    {
        var tokens = new List<CancellationToken>();
        RunAnnex(async (_, ct) =>
        {
            tokens.Add(ct);
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return Reply(HttpStatusCode.OK, "{\"late\":true}");   // a server that answers anyway
        }, (w, frames) =>
        {
            w.HandleMessage("{\"type\":\"annex-stats\"}");
            var first = w.ArcademyLastAnnex;
            var until = DateTime.UtcNow.AddSeconds(5);
            while (tokens.Count < 1 && DateTime.UtcNow < until) Thread.Sleep(10);
            Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now));
            Assert.True(tokens[0].IsCancellationRequested);   // now, not at the 6 s deadline
            Settle(first);
            Assert.Equal(JTokenType.Null, Assert.Single(AnnexReplies(frames))!.Type);
            w.HandleMessage("{\"type\":\"annex-stats\"}");
            var stale = w.ArcademyLastAnnex;
            until = DateTime.UtcNow.AddSeconds(5);
            while (tokens.Count < 2 && DateTime.UtcNow < until) Thread.Sleep(10);
            w.Close();
            Assert.True(tokens[1].IsCancellationRequested);
            Settle(stale);
            Assert.Single(AnnexReplies(frames));
        });
    }
}
