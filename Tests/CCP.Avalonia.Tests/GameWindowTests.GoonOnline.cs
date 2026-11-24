using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonOnline_InitAndRelayUseAccountAndRejectOtherPaths(bool paid) => Run((_, launcher) =>
    {
        var oldUid = CoreAccount.UnifiedUserId;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldSecrets = CoreSecrets.RetrieveProvider;
        var oldHttp = GameWindow.GoonHttpClient;
        var fake = new GrHttp();
        using var http = new HttpClient(fake);
        try
        {
            CoreAccount.UnifiedUserId = "test-user";
            CoreAccount.HasPremiumAccessProvider = () => paid;
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "test-token" : null;
            GameWindow.GoonHttpClient = () => http;
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            var init = Assert.Single(frames, x => (string?)x["type"] == "init");
            Assert.Equal(paid, (bool?)init["caps"]?["canHost"]);
            Assert.True((bool?)init["caps"]?["canJoin"]);
            Assert.NotEqual(true, (bool?)init["practiceOnly"]);
            w.HandleMessage("{\"type\":\"net-post\",\"id\":\"ok\",\"path\":\"/v2/goon/invite\",\"body\":{\"a\":1}}");
            w.HandleMessage("{\"type\":\"net-post\",\"id\":\"bad\",\"path\":\"/v2/goon/../user/me\",\"body\":{}}");
            for (int i = 0; i < 1000 && frames.Count(x => (string?)x["type"] == "net-post-result") < 2; i++)
            { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            var replies = frames.Where(x => (string?)x["type"] == "net-post-result").ToArray();
            Assert.Equal(2, replies.Length);
            Assert.Contains(replies, x => (string?)x["id"] == "ok" && (int?)x["status"] == 200 && (string?)x["body"] == fake.Reply);
            Assert.Contains(replies, x => (string?)x["id"] == "bad" && (int?)x["status"] == 0 && (string?)x["body"] == "forbidden_path");
            var request = Assert.Single(fake.Seen);
            Assert.Equal("POST", request.Method);
            Assert.Equal("https://codebambi-proxy.vercel.app/v2/goon/invite", request.Url);
            Assert.Equal("{\"a\":1}", request.Body);
            Assert.Equal("test-token", request.Token);
            w.Close();
        }
        finally
        {
            GameWindow.GoonHttpClient = oldHttp;
            CoreAccount.UnifiedUserId = oldUid;
            CoreAccount.HasPremiumAccessProvider = oldPremium;
            CoreSecrets.RetrieveProvider = oldSecrets;
        }
    });

    private sealed class DelayedGoonReply : System.Net.Http.HttpMessageHandler
    {
        internal readonly System.Threading.Tasks.TaskCompletionSource<bool> Entered = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly System.Threading.Tasks.TaskCompletionSource<bool> Release = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.TrySetResult(true);
            await Release.Task;
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("late") };
        }
    }

    [Fact]
    public void GoonOnline_LateResponseDoesNotReachClosedWindow() => Run((_, launcher) =>
    {
        var oldHttp = GameWindow.GoonHttpClient;
        var delayed = new DelayedGoonReply();
        using var http = new HttpClient(delayed);
        try
        {
            GameWindow.GoonHttpClient = () => http;
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var replies = new List<JObject>();
            w.Posted += json => { var frame = JObject.Parse(json); if ((string?)frame["type"] == "net-post-result") replies.Add(frame); };
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"net-post\",\"id\":\"late\",\"path\":\"/v2/goon/open\",\"body\":{}}");
            Assert.True(delayed.Entered.Task.Wait(TimeSpan.FromSeconds(10)));
            w.Close();
            delayed.Release.TrySetResult(true);
            for (int i = 0; i < 50; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Assert.Empty(replies);
        }
        finally { delayed.Release.TrySetResult(true); GameWindow.GoonHttpClient = oldHttp; }
    });

    [Fact]
    public void GoonEscape_FirstPausesSecondPanics() => Run((shell, launcher) =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.PanicKey, settings.PanicKeyEnabled);
        try
        {
            (settings.PanicKey, settings.PanicKeyEnabled) = ("Escape", true);
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(w, GameWindow.EscapePauseInFront());
            w.HandleMessage("{\"type\":\"ready\"}");
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            var t = new DateTime(2026, 1, 1, 12, 0, 0);
            shell.HandlePanicKeyPress(t);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            Assert.Single(sent, x => (string?)x["type"] == "kept-escape");
            shell.HandlePanicKeyPress(t.AddSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
        }
        finally { (settings.PanicKey, settings.PanicKeyEnabled) = old; }
    });
}
