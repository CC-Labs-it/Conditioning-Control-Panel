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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The T&amp;C save carry (WPF TcHostService.Save.cs, main b523af076d, e7cf5a7ccc): host-built PUT/GET through
/// the desktop door, answers mapped to save-result / save-incoming, Offline mode stands it down before any request.
/// The transport is a fake: nothing reaches the proxy and nothing is written to the profile.</summary>
public sealed partial class GameWindowTests
{
    private sealed class TcSaveHttp : HttpMessageHandler
    {
        internal readonly List<(string Method, string Path, string Body, string Token)> Calls = new();
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Reply = "{\"ok\":true}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Headers.GetValues(PbpNetRelay.TokenHeader).Single()));
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }

    /// <summary>RunTc signed in as "tc-uid" with a fake transport; every seam and account field is put back.</summary>
    private static void RunTcSave(Action<MainShellWindow, GameWindow, List<JObject>, TcSaveHttp> body) => RunTc("small", (shell, w, sent) =>
    {
        var old = (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, CoreSettings.Current.OfflineMode);
        using var transport = new TcSaveHttp();
        using var http = new HttpClient(transport);
        try
        {
            GameWindow.PbpHttpClient = () => http;
            GameWindow.PbpServerBase = () => "http://tc-save.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;
            CoreAccount.UnifiedUserId = "tc-uid";
            CoreSettings.Current.OfflineMode = false;
            body(shell, w, sent, transport);
        }
        finally
        {
            (GameWindow.PbpHttpClient, GameWindow.PbpServerBase, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, CoreSettings.Current.OfflineMode) = old;
        }
    });

    private static JObject TcSaveAnswer(GameWindow w, List<JObject> sent)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!w.TcSavePending.IsCompleted && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(w.TcSavePending.IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
        return sent.Last(f => (string?)f["type"] is "save-result" or "save-incoming");
    }

    [Fact]
    public void TcSave_SlotIsOpenOnlySignedInAndOnline() => RunTcSave((_, w, sent, _) =>
    {
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.True((bool)sent.Last()["caps"]!["saveSlot"]!);
        CoreSettings.Current.OfflineMode = true;
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.False((bool)sent.Last()["caps"]!["saveSlot"]!);
        CoreSettings.Current.OfflineMode = false;
        CoreAccount.UnifiedUserId = null;
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.False((bool)sent.Last()["caps"]!["saveSlot"]!);
    });

    [Fact]
    public void TcSave_OfferPutsTheHostBuiltBody_AndAnswersSaveResult() => RunTcSave((_, w, sent, transport) =>
    {
        w.HandleMessage("{\"type\":\"save-offer\",\"save\":\"{\\\"v\\\":2}\",\"unified_id\":\"someone-else\"}");
        var answer = TcSaveAnswer(w, sent);
        Assert.Equal("save-result", (string?)answer["type"]);
        Assert.True((bool)answer["ok"]!);
        var call = Assert.Single(transport.Calls);
        Assert.Equal(("PUT", "/v2/tc/save", "test-token"), (call.Method, call.Path, call.Token));
        Assert.True(JToken.DeepEquals(JObject.Parse(TcSaveRules.PutBody("tc-uid", "{\"v\":2}")), JObject.Parse(call.Body)));

        transport.Status = HttpStatusCode.Unauthorized;
        w.HandleMessage("{\"type\":\"save-offer\",\"save\":\"x\"}");
        Assert.Equal(TcSaveRules.ReasonSignIn, (string?)TcSaveAnswer(w, sent)["reason"]);
    });

    [Fact]
    public void TcSave_AskGetsThisAccountsSlot_AndHandsItOverUnchanged() => RunTcSave((_, w, sent, transport) =>
    {
        transport.Reply = "{\"ok\":true,\"save\":\"{\\\"v\\\":2}\",\"savedAt\":1763800000000,\"from\":\"web\"}";
        w.HandleMessage("{\"type\":\"save-ask\"}");
        var answer = TcSaveAnswer(w, sent);
        Assert.Equal("save-incoming", (string?)answer["type"]);
        Assert.Equal("{\"v\":2}", (string?)answer["save"]);
        Assert.Equal(1763800000000L, (long)answer["savedAt"]!);
        var call = Assert.Single(transport.Calls);
        Assert.Equal(("GET", "/v2/tc/save?unified_id=tc-uid", ""), (call.Method, call.Path, call.Body));
    });

    // e7cf5a7ccc: Offline mode answers offline before any request is built.
    [Fact]
    public void TcSave_OfflineModeStandsTheCarryDown() => RunTcSave((_, w, sent, transport) =>
    {
        CoreSettings.Current.OfflineMode = true;
        w.HandleMessage("{\"type\":\"save-offer\",\"save\":\"x\"}");
        w.HandleMessage("{\"type\":\"save-ask\"}");
        Dispatcher.UIThread.RunJobs();
        var answers = sent.Where(f => (string?)f["type"] == "save-result").ToList();
        Assert.Equal(2, answers.Count);
        Assert.All(answers, a => Assert.Equal(TcSaveRules.ReasonOffline, (string?)a["reason"]));
        Assert.Empty(transport.Calls);
    });

    [Fact]
    public void TcSave_SignedOutOrTooBigNeverSends() => RunTcSave((_, w, sent, transport) =>
    {
        w.HandleMessage(new JObject { ["type"] = "save-offer", ["save"] = new string('a', TcRules.MaxSaveChars + 1) }.ToString());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TcSaveRules.ReasonTooBig, (string?)sent.Last()["reason"]);
        CoreAccount.UnifiedUserId = null;
        w.HandleMessage("{\"type\":\"save-ask\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(TcSaveRules.ReasonSignIn, (string?)sent.Last()["reason"]);
        Assert.Empty(transport.Calls);
    });

    [Fact]
    public void TcSave_AnAnswerAfterCloseIsDropped() => RunTcSave((_, w, sent, _) =>
    {
        w.HandleMessage("{\"type\":\"save-ask\"}");
        var pending = w.TcSavePending;
        w.Close();
        Dispatcher.UIThread.RunJobs();
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!pending.IsCompleted && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(sent, f => (string?)f["type"] is "save-result" or "save-incoming");
    });
}
