using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// Restack b154: the DtRH engagement history (#3727) and saved Loom spirals (#3949) on the trunk host.
public sealed partial class GameWindowTests
{
    private const string DtrhTinyGif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>WPF DtrhHostService.cs:309-318,400-404: page deltas reach the store;
    /// the next ready seeds the page with twelve ranked names, only when history exists.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dtrh_EngagementDeltas_AndFavorites(bool hasHistory) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var expected = hasHistory ? new List<string> { "grabbed.gif", "watched.png" } : new List<string>();
        var merged = new List<JObject>();
        GameWindow.DtrhMergeAssetStats = o => merged.Add(o);
        GameWindow.DtrhFavorites = count => { Assert.Equal(12, count); return expected; };
        var w = GameWindow.Launch("dtrh")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"asset-stats\",\"stats\":[{\"name\":\"grabbed.gif\",\"grabs\":3}]}");
        Assert.Equal(3, (int)Assert.Single(merged)["stats"]![0]!["grabs"]!);
        Assert.Empty(sent);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "init", "manifest", "loom-list" }, sent.Take(3).Select(o => (string?)o["type"]));
        var favorites = sent.Where(o => (string?)o["type"] == "favorites").ToList();
        if (hasHistory)
            Assert.Equal(expected, Assert.Single(favorites)["names"]!.Values<string>());
        else Assert.Empty(favorites);
        w.Close();
    });

    /// <summary>WPF DtrhHostService.cs:318,403: telemetry failures do not block the game bridge.</summary>
    [Fact]
    public void Dtrh_EngagementStoreFailure_DoesNotBlockPlay() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        GameWindow.DtrhFavorites = _ => throw new IOException("read failed");
        GameWindow.DtrhMergeAssetStats = _ => throw new IOException("write failed");
        var w = GameWindow.Launch("dtrh")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"asset-stats\",\"stats\":[]}");
        w.HandleMessage("{\"type\":\"request-run\"}");
        Assert.True(w.DtrhReady);
        Assert.Equal(new[] { "init", "manifest", "loom-list", "run-config" }, sent.Select(o => (string?)o["type"]));
        w.Close();
    });

    /// <summary>The helper restores the engagement seams even when a test body throws.</summary>
    [Fact]
    public void Dtrh_EngagementSeams_AreRestoredAfterAFailedBody()
    {
        var (favorites, merge) = (GameWindow.DtrhFavorites, GameWindow.DtrhMergeAssetStats);
        Assert.Throws<InvalidOperationException>(() => RunDtrh(true, (_, _, _, _) =>
        {
            Assert.Empty(GameWindow.DtrhFavorites(12));
            GameWindow.DtrhMergeAssetStats(new JObject());
            GameWindow.DtrhFavorites = _ => throw new IOException("test read");
            GameWindow.DtrhMergeAssetStats = _ => throw new IOException("test write");
            throw new InvalidOperationException("test body failed");
        }));
        Assert.Same(favorites, GameWindow.DtrhFavorites);
        Assert.Same(merge, GameWindow.DtrhMergeAssetStats);
    }

    /// <summary>WPF DtrhHostService.cs:309,405-433,462-479: seed the saved spiral pool,
    /// answer Loom commands and refresh only after successful changes.</summary>
    [Fact]
    public void Dtrh_Loom_SeedsPoolAndHandlesCommands() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
        var oldReveal = ExternalOpener.SelectInFileManager;
        var oldShell = ExternalOpener.Shell;
        string? revealed = null;
        try
        {
            Assert.True(DtrhLoomStore.Save(slug, DtrhTinyGif, JObject.Parse("{\"arms\":3}"), false).Ok);
            ExternalOpener.SelectInFileManager = path => { revealed = path; return true; };
            ExternalOpener.Shell = _ => true;
            var w = GameWindow.Launch("dtrh")!;
            var server = GameWindow.Server();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl!);
            void Send(object frame) => w.Web.OnWebMessage(Newtonsoft.Json.JsonConvert.SerializeObject(frame));
            Send(new { type = "ready" });
            Assert.Equal(new[] { "init", "manifest", "loom-list" }, sent.Select(o => (string?)o["type"]));
            var entry = sent[2]["spirals"]!.Single(s => (string?)s["slug"] == slug);
            var path = DtrhLoomStore.GifPathFor(slug)!;
            Assert.Equal(3, (int)entry["params"]!["arms"]!);
            Assert.Equal(path, server.ResolveFile(new Uri((string)entry["url"]!).AbsolutePath));

            sent.Clear();
            Send(new { type = "loom-save", name = slug, gifBase64 = DtrhTinyGif });
            var failed = Assert.Single(sent);
            Assert.Equal(("loom-result", "save", false, "exists"), ((string?)failed["type"], (string?)failed["op"], (bool)failed["ok"]!, (string?)failed["error"]));
            sent.Clear();
            Send(new { type = "loom-save", name = slug, gifBase64 = DtrhTinyGif, overwrite = true, @params = new { arms = 5 } });
            Assert.Equal(new[] { "loom-result", "loom-list" }, sent.Select(o => (string?)o["type"]));
            Assert.True((bool)sent[0]["ok"]!);
            Assert.Equal(5, (int)sent[1]["spirals"]!.Single(s => (string?)s["slug"] == slug)["params"]!["arms"]!);
            sent.Clear();
            Send(new { type = "loom-reveal", slug = "../outside" });
            Assert.Null(revealed);
            Send(new { type = "loom-reveal", slug });
            Assert.Equal(path, revealed);
            Assert.Empty(sent);
            Send(new { type = "loom-delete", slug });
            Assert.Equal(new[] { "loom-result", "loom-list" }, sent.Select(o => (string?)o["type"]));
            Assert.Equal(("delete", true, slug), ((string?)sent[0]["op"], (bool)sent[0]["ok"]!, (string?)sent[0]["slug"]));
            Assert.DoesNotContain(sent[1]["spirals"]!, s => (string?)s["slug"] == slug);
            sent.Clear();
            Send(new { type = "loom-delete", slug });
            var deleted = Assert.Single(sent);
            Assert.Equal(("delete", false), ((string?)deleted["op"], (bool)deleted["ok"]!));
            w.Close();
        }
        finally
        {
            ExternalOpener.SelectInFileManager = oldReveal;
            ExternalOpener.Shell = oldShell;
            DtrhLoomStore.Delete(slug);
        }
    });

    /// <summary>WPF DtrhHostService.cs:464-479: a failed list post must not stop ready.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dtrh_LoomListFailure_DoesNotBlockFavorites(bool failOnReady) => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var slug = "test-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            GameWindow.DtrhFavorites = _ => new List<string> { "favorite.gif" };
            var w = GameWindow.Launch("dtrh")!;
            var sent = new List<JObject>();
            var failList = failOnReady;
            w.Posted += json =>
            {
                var message = JObject.Parse(json);
                if (failList && (string?)message["type"] == "loom-list") throw new IOException("list post failed");
                sent.Add(message);
            };
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.True(w.DtrhReady);
            Assert.Equal(failOnReady ? new[] { "init", "manifest", "favorites" } : new[] { "init", "manifest", "loom-list", "favorites" },
                sent.Select(o => (string?)o["type"]));
            failList = true;
            w.HandleMessage(Newtonsoft.Json.JsonConvert.SerializeObject(new { type = "loom-save", name = slug, gifBase64 = DtrhTinyGif }));
            Assert.True((bool)sent.Last()["ok"]!);
            Assert.NotNull(DtrhLoomStore.GifPathFor(slug));
            w.Close();
        }
        finally { DtrhLoomStore.Delete(slug); }
    });

    // Restack b154 (#4128): the host keeps its fullscreen state true to the window.

    [Fact]
    public void Dtrh_Fullscreen_RepeatedRequestEchoesHostState() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.WindowState = global::Avalonia.Controls.WindowState.Maximized;
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        Assert.True((bool)Assert.Single(sent)["on"]!);
        Assert.Equal("fullscreen", (string?)sent[0]["type"]);
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.True((bool)Assert.Single(sent)["on"]!);
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\"}");
        Assert.Equal(global::Avalonia.Controls.WindowState.Maximized, w.WindowState);
        Assert.False((bool)Assert.Single(sent)["on"]!);
    });

    [Fact]
    public void Dtrh_Fullscreen_NativeExitUpdatesPageBeforeNextToggle() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        // WPF DtrhHostService.cs:488-502 echoes the actual host state. The page
        // uses that echo to choose the next toggle request.
        var w = GameWindow.Launch("dtrh")!;
        w.HandleMessage("{\"type\":\"ready\"}");
        var sent = new List<JObject>();
        bool pageFullscreen = false;
        w.Posted += json =>
        {
            var frame = JObject.Parse(json);
            sent.Add(frame);
            if ((string?)frame["type"] == "fullscreen") pageFullscreen = (bool)frame["on"]!;
        };
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.True(pageFullscreen);
        Assert.Single(sent); // the host-driven transition has one echo
        sent.Clear();
        w.WindowState = global::Avalonia.Controls.WindowState.Normal; // native exit
        var notification = Assert.Single(sent);
        Assert.Equal("fullscreen", (string?)notification["type"]);
        Assert.False((bool)notification["on"]!);
        Assert.False(pageFullscreen);
        sent.Clear();
        w.WindowState = global::Avalonia.Controls.WindowState.Maximized;
        Assert.Empty(sent); // windowed to windowed is not a fullscreen edge
        w.HandleMessage(new JObject { ["type"] = "fullscreen-set", ["on"] = !pageFullscreen }.ToString());
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        Assert.True(pageFullscreen);
        Assert.Single(sent);
        sent.Clear();
        w.WindowState = global::Avalonia.Controls.WindowState.Normal;
        Assert.False(pageFullscreen);
        Assert.Single(sent);
        sent.Clear();
        w.WindowState = global::Avalonia.Controls.WindowState.FullScreen; // native entry
        Assert.True(pageFullscreen);
        Assert.Single(sent);
    });

    [Fact]
    public void Dtrh_Fullscreen_ExternalExitCanEnterAgain() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        w.WindowState = global::Avalonia.Controls.WindowState.Normal; // the window manager exited fullscreen
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        Assert.True((bool)Assert.Single(sent)["on"]!);
        w.WindowState = global::Avalonia.Controls.WindowState.Normal;
        w.Position = new global::Avalonia.PixelPoint(120, 140); // a move after the native exit must be remembered
        w.WindowState = global::Avalonia.Controls.WindowState.Maximized;
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Assert.Equal(global::Avalonia.Controls.WindowState.Maximized, w.WindowState);
        w.WindowState = global::Avalonia.Controls.WindowState.Normal;
        Assert.Equal(new global::Avalonia.PixelPoint(120, 140), w.Position);
    });
}
