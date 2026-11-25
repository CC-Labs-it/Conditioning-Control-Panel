using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// Ported from #5720 (s10-views-tab-play-1): the Goon net-post relay, stakes and cache URLs.
public sealed partial class GameWindowTests
{
    /// <summary>A Goon window whose page navigation is confirmed and whose ready went through the real gate.</summary>
    private static GameWindow ConfirmedGoon(List<JObject> frames)
    {
        var w = GameWindow.Launch(GameWindow.GoonId)!;
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        return w;
    }

    private static void WaitFrames(List<JObject> frames, string type, int count)
    {
        for (int i = 0; i < 1000 && frames.Count(x => (string?)x["type"] == type) < count; i++)
        { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
    }

    private static void WithGoonAccount(Action body, bool paid = true)
    {
        var (oldUid, oldPremium, oldSecrets, oldHttp, oldLogged) = (CoreAccount.UnifiedUserId, CoreAccount.HasPremiumAccessProvider,
            CoreSecrets.RetrieveProvider, GameWindow.GoonHttpClient, CoreAccount.IsLoggedInProvider);
        try
        {
            CoreAccount.UnifiedUserId = "test-user";
            CoreAccount.HasPremiumAccessProvider = () => paid;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "test-token" : null;
            body();
        }
        finally
        {
            GameWindow.GoonHttpClient = oldHttp;
            (CoreAccount.UnifiedUserId, CoreAccount.HasPremiumAccessProvider, CoreSecrets.RetrieveProvider, CoreAccount.IsLoggedInProvider)
                = (oldUid, oldPremium, oldSecrets, oldLogged);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonOnline_InitAndRelayUseAccountAndRejectOtherPaths(bool paid) => Run((_, launcher) => WithGoonAccount(() =>
    {
        var fake = new GrHttp();
        using var http = new HttpClient(fake);
        GameWindow.GoonHttpClient = () => http;
        launcher.Hide();
        var frames = new List<JObject>();
        var w = ConfirmedGoon(frames);
        var init = Assert.Single(frames, x => (string?)x["type"] == "init");
        Assert.Equal("test-token", (string?)init["net"]?["authToken"]);
        Assert.Equal(paid, (bool?)init["caps"]?["mediaTransfer"]);
        Assert.Equal(paid, (bool?)init["caps"]?["canHost"]);
        Assert.True((bool?)init["caps"]?["canJoin"]);
        w.Web.OnWebMessage("{\"type\":\"net-post\",\"id\":\"ok\",\"path\":\"/v2/goon/invite\",\"body\":{\"a\":1}}");
        w.Web.OnWebMessage("{\"type\":\"net-post\",\"id\":\"bad\",\"path\":\"/v2/goon/../user/me\",\"body\":{}}");
        WaitFrames(frames, "net-post-result", 2);
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
    }, paid));

    [Fact]
    public void GoonOnline_TokenAndRelayNeverReachAnotherDocument() => Run((_, launcher) => WithGoonAccount(() =>
    {
        var fake = new GrHttp();
        using var http = new HttpClient(fake);
        GameWindow.GoonHttpClient = () => http;
        launcher.Hide();
        var frames = new List<JObject>();
        var w = GameWindow.Launch(GameWindow.GoonId)!;
        w.Posted += json => frames.Add(JObject.Parse(json));
        const string post = "{\"type\":\"net-post\",\"id\":\"x\",\"path\":\"/v2/goon/invite\",\"body\":{}}";
        // Not yet confirmed: a ready reaching the handler gets no token, a post goes nowhere.
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("", (string?)Assert.Single(frames, x => (string?)x["type"] == "init")["net"]?["authToken"]);
        w.HandleMessage(post);
        // Another document in the window: nothing it sends is relayed or answered, nor gets a token.
        var other = new Uri(w.PageUrl!, "/other.html");
        var start = new WebViewNavigationStartingEventArgs { Request = other };
        w.Web.OnNavigationStarted(null, start);
        Assert.False(start.Cancel);
        w.Web.OnWebMessage(post);
        w.Web.OnNavigationCompleted(other);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        w.Web.OnWebMessage(post);
        w.HandleMessage(post);
        for (int i = 0; i < 30; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Empty(fake.Seen);
        Assert.DoesNotContain(frames, x => (string?)x["type"] == "net-post-result");
        Assert.DoesNotContain(frames, x => (string?)x["net"]?["authToken"] is { Length: > 0 });
        w.Close();
    }));

    private sealed class DelayedGoonReply : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<CancellationToken> Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Entered.TrySetResult(ct);
            await Release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("late") };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonOnline_LateResponseDoesNotReachClosedWindow(bool panic) => Run((shell, launcher) => WithGoonAccount(() =>
    {
        var delayed = new DelayedGoonReply();
        using var http = new HttpClient(delayed);
        try
        {
            GameWindow.GoonHttpClient = () => http;
            launcher.Hide();
            var replies = new List<JObject>();
            var w = ConfirmedGoon(replies);
            w.Web.OnWebMessage("{\"type\":\"net-post\",\"id\":\"late\",\"path\":\"/v2/goon/open\",\"body\":{}}");
            Assert.True(delayed.Entered.Task.Wait(TimeSpan.FromSeconds(10)));
            if (panic) PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            else w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.True(delayed.Entered.Task.Result.IsCancellationRequested);   // the request itself is cancelled
            delayed.Release.TrySetResult(true);
            for (int i = 0; i < 50; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Assert.DoesNotContain(replies, x => (string?)x["type"] == "net-post-result");
        }
        finally { delayed.Release.TrySetResult(true); }
    }));

    [Fact]
    public void GoonInit_SendsOnlyTheLeaderboardNameNeverTheProviderName() => Run((_, launcher) =>
    {
        var oldProvider = CoreAccount.DisplayNameProvider;
        var oldName = CoreSettings.Current.UserDisplayName;
        try
        {
            CoreAccount.DisplayNameProvider = () => "Real Patreon Name";
            launcher.Hide();
            foreach (var (stored, expected) in new[] { ((string?)null, "Player"), ("  ", "Player"), ("Leader", "Leader") })
            {
                CoreSettings.Current.UserDisplayName = stored;
                var frames = new List<JObject>();
                var w = ConfirmedGoon(frames);
                Assert.Equal(expected, (string?)Assert.Single(frames, x => (string?)x["type"] == "init")["identity"]?["displayName"]);
                w.Close();
            }
        }
        finally { CoreAccount.DisplayNameProvider = oldProvider; CoreSettings.Current.UserDisplayName = oldName; }
    });

    [Fact]
    public void GoonStake_LimitsFrameReachesBridgeAndReplies() => Run((_, launcher) =>
    {
        var old = GameWindow.GoonStakeBridge;
        var fake = new FakeStakes();
        using var http = new HttpClient(fake);
        try
        {
            GameWindow.GoonStakeBridge = (game, post) => new StakeBridge(game, post,
                new StakeApi(http, () => ("test-user", "test-token"), "http://stakes.test"),
                new StakeSettlement((_, seconds) => seconds, null, () => "test-user"), () => false, () => { });
            launcher.Hide();
            var w = GameWindow.Launch(GameWindow.GoonId)!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"stake-limits\"}");
            Assert.True(GameWindow.GoonStakePending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            var reply = Assert.Single(frames, x => (string?)x["type"] == "stake");
            Assert.Equal("goon", (string?)reply["game"]);
            Assert.Equal("limits", (string?)reply["op"]);
            Assert.Equal("limits", Assert.Single(fake.Calls).Op);
            w.Close();
        }
        finally { GameWindow.GoonStakeBridge = old; }
    });

    [Fact]
    public void GoonTransfer_CacheUrlsAreUsableWithoutCookiesAndCannotExposeOtherFiles() => Run((_, launcher) =>
    {
        var root = Directory.CreateTempSubdirectory("goon-cache-test-").FullName;
        var cache = Path.Combine(root, "cache");
        var art = Directory.CreateDirectory(Path.Combine(cache, "art")).FullName;
        var name = new string('a', 64) + ".jpg";
        var bytes = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(Path.Combine(art, name), bytes);
        File.WriteAllText(Path.Combine(cache, "index.json"), "{}");
        using var server = new WebAssetServer(root) { GoonCacheRoot = () => cache };
        try
        {
            launcher.Hide();
            var w = new GameWindow(GameWindow.Games[GameWindow.GoonId]);
            w.Load(server); w.Show();
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.PostGoonCache(new { type = "cache-list", items = new[] { new { artUrl = "https://ccp.cache/art/" + name, srcUrl = "https://ccp.assets/pictures/a.jpg" } } });
            Dispatcher.UIThread.RunJobs();
            var item = Assert.Single(frames, x => (string?)x["type"] == "cache-list")["items"]![0]!;
            var url = (string)item["artUrl"]!;
            Assert.StartsWith("http://127.0.0.1:", url);
            Assert.Contains("/ccp.assets/pictures/a.jpg?ccp_t=", (string)item["srcUrl"]!);
            using var http = new HttpClient(new HttpClientHandler { UseCookies = false });
            Assert.Equal(bytes, http.GetByteArrayAsync(url).GetAwaiter().GetResult());
            var stripped = new Uri(url).GetLeftPart(UriPartial.Path);
            Assert.Equal(HttpStatusCode.Forbidden, http.GetAsync(stripped).GetAwaiter().GetResult().StatusCode);
            Assert.Null(server.ResolveFile("/__goon_cache__/art/../index.json"));
            Assert.Null(server.ResolveFile("/__goon_cache__/index.json"));
            Assert.Null(server.ResolveFile("/__goon_cache__/tmp/" + name));
            if (!OperatingSystem.IsWindows())
            {
                var link = new string('b', 64) + ".jpg";
                File.CreateSymbolicLink(Path.Combine(art, link), Path.Combine(cache, "index.json"));
                Assert.Null(server.ResolveFile("/__goon_cache__/art/" + link));
            }
            w.Close();
        }
        finally { Directory.Delete(root, true); }
    });
}
