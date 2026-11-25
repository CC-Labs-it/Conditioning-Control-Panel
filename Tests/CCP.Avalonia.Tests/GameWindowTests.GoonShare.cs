using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // The cache bridge (attached when the window opens) pushes its own cache-state frames; these tests count
    // the inbox and boot frames only.
    private static void WaitGoonReplies(List<JObject> replies, int count)
    {
        bool Settled() { replies.RemoveAll(r => (string?)r["type"] == "cache-state"); return replies.Count >= count; }
        for (var i = 0; i < 1000 && !Settled(); i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(count, replies.Count);
    }

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
        WaitGoonReplies(replies, 1);
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
        WaitGoonReplies(replies, 3);
        Assert.All(replies, x => Assert.True((bool)x["ok"]!));
        Send(new { type = "goon-recv-commit", id });
        WaitGoonReplies(replies, 4);
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
        WaitGoonReplies(replies, 5);
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        var other = Guid.NewGuid().ToString("N");
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = 64L * 1024 * 1024 + 1 });
        WaitGoonReplies(replies, 6);
        Assert.Equal("too-big", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-begin", id = other, sha256 = sha, mime = "image/png", bytes = data.Length });
        Send(new { type = "goon-recv-abort", id = other });
        Send(new { type = "goon-recv-chunk", id = other, seq = 0, b64 = "AA==" });
        WaitGoonReplies(replies, 9);
        Assert.Equal("unknown-job", (string?)replies.Last()["error"]);
        Send(new { type = "goon-recv-drop", sha256 = sha });
        WaitGoonReplies(replies, 10);
        Assert.True((bool)replies.Last()["ok"]!);
    });

    [Fact]
    public void GoonInbox_BootCloseAndPanicPurgeEvenDuringCommit() => Run((_, _) =>
    {
        Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
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
        GameWindow? replacement = null;
        w.Load(GameWindow.Server()); w.Show();
        try
        {
            Commit("boot-" + Guid.NewGuid());
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(File.Exists(file));
            Commit("close-" + Guid.NewGuid());
            w.Close();
            Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(File.Exists(file));

            var next = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            next.Load(GameWindow.Server()); next.Show();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int commitStarts = 0;
            GameWindow.GoonInboxBeforeCommit = () => { Interlocked.Increment(ref commitStarts); entered.Set(); release.Wait(); };
            var id = "race-" + Guid.NewGuid();
            Prepare(id);
            next.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(new { type = "goon-recv-commit", id }));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Null(store.Begin("queued-old", new string('0', 64), "image/png", data.Length));
            Assert.Null(store.AppendChunk("queued-old", 0, Convert.ToBase64String(data)));
            next.HandleMessage("{\"type\":\"goon-recv-commit\",\"id\":\"queued-old\"}");
            var previous = PanicSurfaces.All;
            bool laterSurfaceStopped = false;
            PanicSurfaces.All = new[]
            {
                new PanicSurfaces.Surface("games", _ => GameWindow.CloseAllForPanic()),
                new PanicSurfaces.Surface("later", _ => laterSurfaceStopped = true),
            };
            // A timeout releases the worker even with the old blocking close, so the fail-proof cannot hang.
            var unblock = Task.Run(() => { if (!release.Wait(TimeSpan.FromSeconds(3))) release.Set(); });
            try
            {
                var clock = Stopwatch.StartNew();
                PanicSurfaces.StopAll("inbox panic test", aftermathArmed: true);
                clock.Stop();
                Assert.True(laterSurfaceStopped);
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Panic took {clock.Elapsed}");
                Assert.False(GameWindow.GoonInboxIdle.IsCompleted);
                // A new session may start before the old worker finishes. Its media must follow
                // both purges in the queue and survive them.
                replacement = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
                replacement.Load(GameWindow.Server()); replacement.Show();
                replacement.HandleMessage("{\"type\":\"ready\"}");
                var newId = "new-" + Guid.NewGuid();
                void Send(object frame) => replacement.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(frame));
                Send(new { type = "goon-recv-begin", id = newId, sha256 = sha, mime = "image/png", bytes = data.Length });
                Send(new { type = "goon-recv-chunk", id = newId, seq = 0, b64 = Convert.ToBase64String(data) });
                Send(new { type = "goon-recv-commit", id = newId });
            }
            finally { release.Set(); unblock.GetAwaiter().GetResult(); PanicSurfaces.All = previous; }
            Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
            Assert.True(File.Exists(file));
            Assert.Single(store.ListForManifest());
            Assert.Equal(2, Volatile.Read(ref commitStarts)); // in-flight old and new, never the queued old commit
            Assert.Equal("unknown-job", store.AppendChunk("queued-old", 1, "AA=="));
            replacement!.Close();
            Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(File.Exists(file));
            Assert.Empty(store.ListForManifest());
        }
        finally
        {
            GameWindow.GoonInboxBeforeCommit = null;
            replacement?.Close();
            w.Close();
            GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10));
            store.PurgeCommittedSafe("test cleanup");
        }
    });

    [Fact]
    public void GoonInbox_StaleAbortDeletesItsPartialAndExitDrainsPurge() => Run((_, _) =>
    {
        Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
        var store = TransferInboxStore.Instance;
        var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a3sAAAAASUVORK5CYII=");
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var file = Path.Combine(store.RecvDir, sha + ".png");
        var partSha = new string('1', 64);
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        GameWindow.GoonInboxBeforeCommit = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); };
        void Send(object frame) => w.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(frame));
        try
        {
            // A page abort queued behind a slow commit, then a reload: the abort must still run.
            Assert.Null(store.Begin("slow", sha, "image/png", data.Length));
            Assert.Null(store.AppendChunk("slow", 0, Convert.ToBase64String(data)));
            Send(new { type = "goon-recv-commit", id = "slow" });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Null(store.Begin("part", partSha, "image/png", data.Length));
            Assert.Null(store.AppendChunk("part", 0, Convert.ToBase64String(data[..20])));
            Send(new { type = "goon-recv-abort", id = "part" });
            Send(new { type = "ready" });
            release.Set();
            Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
            Assert.Equal("unknown-job", store.AppendChunk("part", 1, Convert.ToBase64String(data[20..])));
            Assert.Empty(Directory.EnumerateFiles(store.TmpDir, "*" + partSha + "*"));

            // App exit with the window still open: queued work is cancelled, media purged, exit waits.
            entered.Reset(); release.Reset();
            Assert.Null(store.Begin("slow2", sha, "image/png", data.Length));
            Assert.Null(store.AppendChunk("slow2", 0, Convert.ToBase64String(data)));
            Send(new { type = "goon-recv-commit", id = "slow2" });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Null(store.Begin("queued", sha, "image/png", data.Length)); // resumes slow2's partial
            Send(new { type = "goon-recv-commit", id = "queued" });
            var unblock = Task.Run(() => { Thread.Sleep(300); release.Set(); });
            Assert.True(GameWindow.DrainGoonInboxForExit(TimeSpan.FromSeconds(10)));
            unblock.GetAwaiter().GetResult();
            Assert.False(File.Exists(file));
            Assert.Empty(store.ListForManifest());
            Assert.Equal("unknown-job", store.AppendChunk("queued", 1, "AA=="));
        }
        finally
        {
            release.Set();
            GameWindow.GoonInboxBeforeCommit = null;
            w.Close();
            GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10));
            store.Abort("part"); store.Abort("queued");
            store.PurgeCommittedSafe("test cleanup");
        }
    });
}
