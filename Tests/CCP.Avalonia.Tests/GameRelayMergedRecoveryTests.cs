using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class RecoveryWire : HttpMessageHandler
    {
        public int Status = 409;
        public string Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(Body) });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GameRelay_RecoversMergedAndStillReplies(bool pbp) => RunGlassroad((_, _, _) =>
    {
        using var saved = new MergedRecoveryState();
        var wire = new RecoveryWire();
        using var http = new HttpClient(wire);
        GameWindow.PbpHttpClient = () => http;
        var settings = new AppSettings { UnifiedId = "u_tombstone1" };
        MergedAccountRecovery.ResetForTest();
        MergedAccountRecovery.SettingsForTest = () => settings;
        int stops = 0;
        MergedAccountRecovery.StopSync = () => Interlocked.Increment(ref stops);
        MergedAccountRecovery.StampQuests = null;
        MergedAccountRecovery.Reauthenticate = null;
        MergedAccountRecovery.ReloadProfile = null;
        MergedAccountRecovery.Finished = null;
        var w = GameWindow.Launch(pbp ? GameWindow.PbpId : GameWindow.GlassroadId)!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        void Send(string id)
        {
            w.Web.OnWebMessage(new JObject { ["type"] = pbp ? "pbp:net" : "net", ["id"] = id,
                ["method"] = "GET", ["path"] = pbp ? "/v2/pbp/lobby" : "/v2/race/access" }.ToString());
            Assert.True((pbp ? w.PbpPending : w.GlassroadPending).Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
        }
        Send("merged");
        Assert.True(MergedAccountRecovery.LastSwap.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(stops == 1, $"stops={stops}; frames={string.Join(";", sent)}");
        Assert.Equal("u_canon123", settings.UnifiedId);
        var reply = sent.Single(x => (string?)x["id"] == "merged");
        Assert.Equal(pbp ? "pbp:net-result" : "net-reply", (string?)reply["type"]);
        Assert.Equal(409, (int)reply["status"]!);
        Assert.Equal(wire.Body, (string?)reply["body"]);
        wire.Body = "{}";
        Send("plain");
        wire.Status = 200;
        Send("ok");
        Assert.Equal(1, stops);
        Assert.Equal(409, (int)sent.Single(x => (string?)x["id"] == "plain")["status"]!);
        Assert.Equal(200, (int)sent.Single(x => (string?)x["id"] == "ok")["status"]!);
        if (pbp)
        {
            wire.Status = 409;
            wire.Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon456\"}";
            MergedAccountRecovery.SettingsForTest = () => throw new InvalidOperationException();
            Send("throw");
            reply = sent.Single(x => (string?)x["id"] == "throw");
            Assert.Equal(0, (int)reply["status"]!);
            Assert.Equal("", (string?)reply["body"]);
        }
        w.Close();
    });
}
