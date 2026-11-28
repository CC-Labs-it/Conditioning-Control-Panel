using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private static void Settle(Task? task)
    {
        Assert.NotNull(task);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task!.IsCompleted && DateTime.UtcNow < until) Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted);
    }

    // WPF ArcademyHostService.cs:5063-5185: local piles are sampled from the library, deselected files left out.
    [Fact]
    public void Arcademy_LocalSampleServesTheActiveLibraryOnly() => Run((_, _) =>
    {
        var lib = Directory.CreateTempSubdirectory("arc-local-").FullName;
        var s = CoreSettings.Current;
        var old = (CorePaths.EffectiveAssetsProvider, s.DisabledAssetPaths.ToHashSet(), s.MediaSource);
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            Directory.CreateDirectory(Path.Combine(lib, "images", "pile"));
            Directory.CreateDirectory(Path.Combine(lib, "videos"));
            File.WriteAllBytes(Path.Combine(lib, "images", "pile", "a.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "off.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "videos", "v.mp4"), new byte[] { 1 });
            CorePaths.EffectiveAssetsProvider = () => lib;
            s.DisabledAssetPaths = new() { "images/off.png" };
            s.MediaSource = "local";   // local piles are not gated on remote media
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"r1\",\"kind\":\"still\",\"count\":8,\"tag\":\"Target!\"}");
            Settle(w.ArcademyLastLocalSample);
            var still = frames.Last(f => (string?)f["reqId"] == "r1");
            Assert.Equal("assets", (string?)still["type"]); Assert.True((bool?)still["done"]); Assert.Equal("target", (string?)still["tag"]);
            var row = Assert.Single((JArray)still["urls"]!);
            Assert.EndsWith("/ccp.assets/images/pile/a.png", (string?)row["url"]);
            Assert.Equal(("still", "image/png", "images/pile"), ((string?)row["kind"], (string?)row["mime"], (string?)row["src"]));
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"r2\",\"kind\":\"loop\",\"folders\":[\"../..\",\"videos\"]}");
            Settle(w.ArcademyLastLocalSample);
            Assert.EndsWith("/ccp.assets/videos/v.mp4", (string?)((JArray)frames.Last()["urls"]!).Single()["url"]);
            // A path that climbs out of the library is refused, so the default roots are used.
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"r3\",\"kind\":\"still\",\"folders\":[\"../..\"]}");
            Settle(w.ArcademyLastLocalSample);
            Assert.Single((JArray)frames.Last()["urls"]!);
        }
        finally { w.Close(); (CorePaths.EffectiveAssetsProvider, s.DisabledAssetPaths, s.MediaSource) = old; Directory.Delete(lib, true); }
    });

    // WPF ArcademyHostService.cs:1498-1511, 1533-1604: init.settings carries the folders and presets SORT's setup
    // door picks from, and the folder or preset it picks is what local-sample-request samples.
    [Fact]
    public void Arcademy_InitOffersLocalPilesThatSortCanSample() => Run((_, _) =>
    {
        var lib = Directory.CreateTempSubdirectory("arc-piles-").FullName;
        var s = CoreSettings.Current;
        var old = (CorePaths.EffectiveAssetsProvider, s.DisabledAssetPaths.ToHashSet(), s.AssetPresets.ToList());
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            Directory.CreateDirectory(Path.Combine(lib, "images", "pile", "deep"));
            Directory.CreateDirectory(Path.Combine(lib, "videos"));
            File.WriteAllBytes(Path.Combine(lib, "images", "pile", "a.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "pile", "deep", "b.gif"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "off.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "videos", "v.mp4"), new byte[] { 1 });
            CorePaths.EffectiveAssetsProvider = () => lib;
            s.DisabledAssetPaths = new() { "images/off.png" };
            s.AssetPresets = new() { new ConditioningControlPanel.Models.AssetPreset { Id = "p1", Name = "Mine", DisabledAssetPaths = new() { "images/pile/a.png" } } };
            w.HandleMessage("{\"type\":\"ready\"}");
            var bag = (JObject)frames.Single(f => (string?)f["type"] == "init")["settings"]!;
            var folders = ((JArray)bag["localFolders"]!).ToDictionary(f => (string)f["path"]!, f => ((int)f["gifs"]!, (int)f["stills"]!, (int)f["videos"]!));
            Assert.Equal(new[] { "images", "images/pile", "images/pile/deep", "videos" }, folders.Keys);
            Assert.Equal((1, 1, 0), folders["images"]);   // off.png is deselected, so it is not counted
            Assert.Equal((1, 1, 0), folders["images/pile"]);
            Assert.Equal((0, 0, 1), folders["videos"]);
            var preset = Assert.Single((JArray)bag["assetPresets"]!);
            Assert.Equal(("p1", "Mine"), ((string?)preset["id"], (string?)preset["name"]));
            // games/sort/setup.js:174-178 opens the local door on a preset, or on a non-root media folder plus a second one.
            Assert.Contains(folders, f => f.Key is not ("images" or "videos") && f.Value != (0, 0, 0));
            Assert.True(folders.Count(f => f.Value != (0, 0, 0)) >= 2);
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"f\",\"kind\":\"still\",\"tag\":\"target\",\"folders\":[\"images/pile\"]}");
            Settle(w.ArcademyLastLocalSample);
            var row = Assert.Single((JArray)frames.Last(f => (string?)f["reqId"] == "f")["urls"]!);
            // Root-relative, so the page's isLocalUrl (provider/inventory.js:37) counts it as local.
            Assert.Equal(("/ccp.assets/images/pile/a.png", "images/pile"), ((string?)row["url"], (string?)row["src"]));
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"p\",\"kind\":\"loop\",\"tag\":\"noise\",\"presetId\":\"p1\"}");
            Settle(w.ArcademyLastLocalSample);
            var urls = ((JArray)frames.Last(f => (string?)f["reqId"] == "p")["urls"]!).Select(r => ((string?)r["url"], (string?)r["src"])).OrderBy(r => r.Item1).ToArray();
            Assert.Equal(new[] { ("/ccp.assets/images/pile/deep/b.gif?ccp_preset=p1", "preset:p1"), ("/ccp.assets/videos/v.mp4?ccp_preset=p1", "preset:p1") }, urls);
            // off.png is out of the current selection but in p1's: the server serves the preset's own sample (WPF ccp.assets serves the folder).
            w.HandleMessage("{\"type\":\"local-sample-request\",\"reqId\":\"o\",\"kind\":\"still\",\"tag\":\"noise\",\"presetId\":\"p1\"}");
            Settle(w.ArcademyLastLocalSample);
            var off = (string)Assert.Single((JArray)frames.Last(f => (string?)f["reqId"] == "o")["urls"]!)["url"]!;
            Assert.Equal("/ccp.assets/images/off.png?ccp_preset=p1", off);
            var web = Directory.CreateTempSubdirectory("arc-web-").FullName;
            try
            {
                using var server = new WebAssetServer(web) { AssetsRoot = () => lib };
                using var http = new System.Net.Http.HttpClient();
                HttpStatusCode Get(string path) => http.GetAsync($"http://127.0.0.1:{server.Port}{path}&ccp_t={server.Token}").GetAwaiter().GetResult().StatusCode;
                Assert.Equal(HttpStatusCode.OK, Get(off));
                Assert.Equal(HttpStatusCode.NotFound, Get("/ccp.assets/images/off.png?x=1"));   // the current selection still holds
                Assert.Equal(HttpStatusCode.NotFound, Get("/ccp.assets/images/pile/a.png?ccp_preset=p1"));   // and p1 its own
            }
            finally { Directory.Delete(web, true); }
        }
        finally { w.Close(); (CorePaths.EffectiveAssetsProvider, s.DisabledAssetPaths, s.AssetPresets) = old; Directory.Delete(lib, true); }
    });

    // WPF ArcademyHostService.cs:5281-5376: one sub-probe per reqId; a found sub joins the library, never the feed.
    [Fact]
    public void Arcademy_ProbeSubAnswersEveryRequestAndKeepsTheFeed() => Run((_, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.RemoteSubLibrary.ToList(), new Dictionary<string, ConditioningControlPanel.Models.RemoteSubVerdict>(s.FypOnlineSubVerdicts),
            s.FypOnlineCustomSubs.ToList(), s.OfflineMode, s.MediaSource, s.RemoteMediaConsented);
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        int calls = 0;
        w.FypProbeOverride = (_, _) => { calls++; return Task.FromResult(new SubProbe { Ok = true, VideoCount = 3 }); };
        try
        {
            s.RemoteSubLibrary = new(); s.FypOnlineSubVerdicts = new(); s.FypOnlineCustomSubs = new();
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"bad\",\"name\":\"!!\"}");
            Assert.Equal(("bad", "invalid"), ((string?)frames.Last()["reqId"], (string?)frames.Last()["error"]));
            s.OfflineMode = true;
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"off\",\"name\":\"cats\"}");
            Assert.Equal(("off", "offline"), ((string?)frames.Last()["reqId"], (string?)frames.Last()["error"]));
            Assert.Equal(0, calls);
            (s.OfflineMode, s.MediaSource, s.RemoteMediaConsented) = (false, "online", true);
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"yes\",\"name\":\"cats\"}");
            Settle(w.ArcademyLastProbe);
            var probe = frames.Single(f => (string?)f["reqId"] == "yes");
            Assert.Equal(("sub-probe", "cats", true, 3), ((string?)probe["type"], (string?)probe["name"], (bool)probe["ok"]!, (int)probe["videoCount"]!));
            Assert.Equal("library", (string?)frames.Last()["type"]);
            Assert.True(s.LibraryHasSub("cats"));
            Assert.Empty(s.FypOnlineCustomSubs);
            // A fresh verdict is answered from the cache without a second round trip.
            w.HandleMessage("{\"type\":\"probe-sub\",\"reqId\":\"again\",\"name\":\"cats\"}");
            Assert.Equal(("again", true), ((string?)frames.Last()["reqId"], (bool)frames.Last()["ok"]!));
            Assert.Equal(1, calls);
        }
        finally
        {
            w.Close();
            (s.RemoteSubLibrary, s.FypOnlineSubVerdicts, s.FypOnlineCustomSubs, s.OfflineMode, s.MediaSource, s.RemoteMediaConsented) = old;
        }
    });

    // WPF ArcademyHostService.cs:5537-5600: a PNG reaches the clipboard, anything else is refused, one reply each.
    [Fact]
    public void Arcademy_ShareImageRepliesOnceWithTheClipboardResult() => Run((_, launcher) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            launcher.Hide(); w.Show();
            using var ms = new MemoryStream();
            using (var bmp = new RenderTargetBitmap(new PixelSize(2, 2))) bmp.Save(ms);
            var png = Convert.ToBase64String(ms.ToArray());
            w.HandleMessage(new JObject { ["type"] = "share-image", ["png"] = png }.ToString());
            Settle(w.ArcademyLastShare);
            Assert.True((bool?)frames.Single(f => (string?)f["type"] == "share-image-result")["ok"]);
            w.HandleMessage(new JObject { ["type"] = "share-image", ["png"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }) }.ToString());
            Settle(w.ArcademyLastShare);
            w.HandleMessage("{\"type\":\"share-image\",\"png\":\"%%%\"}");
            Settle(w.ArcademyLastShare);
            Assert.Equal(new bool?[] { true, false, false }, frames.Where(f => (string?)f["type"] == "share-image-result").Select(f => (bool?)f["ok"]).ToArray());
        }
        finally { w.Close(); }
    });
}
