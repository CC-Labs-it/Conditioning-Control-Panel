using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.GoonGame;
using System.Security.Cryptography;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonInbox_UsesCarrierBeforeNavigation() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        Assert.True(w.Web.InjectCarrier);
        w.Load(GameWindow.Server()); w.Show();
        var replies = new List<JObject>();
        w.Posted += json => replies.Add(JObject.Parse(json));
        var frame = Newtonsoft.Json.JsonConvert.SerializeObject("{\"type\":\"goon-recv-begin\",\"id\":\"x\",\"sha256\":\"invalid\"}");
        w.Web.OnWebMessage(frame);
        Assert.Empty(replies);
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage(frame);
        Assert.Equal("goon-recv-result", (string?)Assert.Single(replies)["type"]);
        Assert.False((bool)replies[0]["ok"]!);
    });

    [Fact]
    public void GoonInbox_ChunkCommitAbortAndOversize() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var replies = new List<JObject>();
        w.Posted += json => { var o = JObject.Parse(json); if ((string?)o["type"] == "goon-recv-result") replies.Add(o); };
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a3sAAAAASUVORK5CYII=");
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var id = Guid.NewGuid().ToString("N");
        void Send(object frame) => w.Web.OnWebMessage(Newtonsoft.Json.JsonConvert.SerializeObject(
            Newtonsoft.Json.JsonConvert.SerializeObject(frame))); // WebKit string carrier
        Send(new { type = "goon-recv-abort", id });
        Assert.Empty(replies); // before navigation, no page may send business frames
        w.Web.OnNavigationCompleted(w.PageUrl);
        Send(new { type = "ready" });
        Send(new { type = "goon-recv-begin", id, sha256 = sha, mime = "image/png", bytes = data.Length });
        Send(new { type = "goon-recv-chunk", id, seq = 0, b64 = Convert.ToBase64String(data[..20]) });
        Send(new { type = "goon-recv-chunk", id, seq = 1, b64 = Convert.ToBase64String(data[20..]) });
        Assert.All(replies, x => Assert.True((bool)x["ok"]!));
        Send(new { type = "goon-recv-commit", id });
        for (var i = 0; i < 500 && replies.Count < 4; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(4, replies.Count);
        Assert.True((bool)replies.Last()["ok"]!);
        Assert.Equal(data.Length, (long)replies.Last()["bytes"]!);
        var url = (string)replies.Last()["url"]!;
        Assert.StartsWith("/__goon_recv__/", url);
        using (var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }))
        {
            var server = GameWindow.Server();
            var origin = new Uri(server.Url("goon/index.html"));
            Assert.Equal(HttpStatusCode.Forbidden, http.GetAsync(new Uri(origin, url)).GetAwaiter().GetResult().StatusCode);
            http.GetAsync(origin).GetAwaiter().GetResult().Dispose(); // token establishes the page cookie
            var response = http.GetAsync(new Uri(origin, url)).GetAwaiter().GetResult();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            Assert.Equal(data, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            Assert.Null(server.ResolveFile("/__goon_recv__/../recv_index.json"));
        }
        Send(new { type = "goon-recv-commit", id });
        for (var i = 0; i < 500 && replies.Count < 5; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        var other = Guid.NewGuid().ToString("N");
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = 64L * 1024 * 1024 + 1 });
        Assert.Equal("too-big", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = data.Length });
        Send(new { type = "goon-recv-abort", id = other });
        Send(new { type = "goon-recv-chunk", id = other, seq = 0, b64 = "AA==" });
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-drop", sha256 = sha });
        Assert.True((bool)replies.Last()["ok"]!);
    });

    [Fact]
    public void GoonInbox_BootCloseAndPanicPurgeEvenDuringCommit() => Run((_, _) =>
    {
        var store = TransferInboxStore.Instance;
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a3sAAAAASUVORK5CYII=");
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var file = Path.Combine(store.RecvDir, sha + ".png");
        void Prepare(string id)
        {
            Assert.Null(store.Begin(id, sha, "image/png", data.Length));
            Assert.Null(store.AppendChunk(id, 0, Convert.ToBase64String(data)));
        }
        void Commit(string id) { Prepare(id); Assert.True(store.Commit(id).Ok); Assert.True(File.Exists(file)); }
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        try
        {
            Commit("boot-" + Guid.NewGuid());
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.False(File.Exists(file));
            Commit("close-" + Guid.NewGuid());
            w.Close();
            Assert.False(File.Exists(file));

            var next = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            next.Load(GameWindow.Server()); next.Show();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            GameWindow.GoonInboxBeforeCommit = () => { entered.Set(); release.Wait(); };
            var id = "race-" + Guid.NewGuid();
            Prepare(id);
            next.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(new { type = "goon-recv-commit", id }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var unblock = Task.Run(() => { Thread.Sleep(150); release.Set(); });
            GameWindow.CloseAllForPanic();
            unblock.GetAwaiter().GetResult();
            Assert.False(File.Exists(file));
            Assert.Empty(store.ListForManifest());
        }
        finally
        {
            GameWindow.GoonInboxBeforeCommit = null;
            w.Close();
            store.PurgeCommittedSafe("test cleanup");
        }
    });
}
