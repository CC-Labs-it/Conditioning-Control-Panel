using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Sparkles.Claims;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>T&amp;C pictures, the library and the medal (WPF TcHostService.Media.cs, main ed66bf9f44, 41c57f2de9,
/// 7c9a76bca3): the consent gate, the theme subs, the feed from this page's origin, Use local files, panic.</summary>
public sealed partial class GameWindowTests
{
    private static void RunTcMedia(Action<MainShellWindow, GameWindow, List<JObject>, List<PicturePool>> body) => RunTc("full", (shell, w, sent) =>
    {
        var s = CoreSettings.Current;
        var old = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode);
        var factory = GameWindow.TcPoolFactory;
        var pools = new List<PicturePool>();
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = ("local", false, false, false);
            GameWindow.TcPoolFactory = cb => { var p = new PicturePool(cb); pools.Add(p); return p; };
            body(shell, w, sent, pools);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.TcPoolFactory = factory;
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = old;
        }
    });

    private static JObject LastOf(List<JObject> sent, string type) => sent.Last(o => (string?)o["type"] == type);

    [Fact]
    public void TcMedia_InitOffersDevicePicturesAndTheLibraryFromThisOrigin() => RunTcMedia((_, w, sent, _) =>
    {
        w.HandleMessage("{\"type\":\"ready\"}");
        var caps = (JObject)LastOf(sent, "init")["caps"]!;
        Assert.Equal("device", (string?)caps["scrolller"]!["origin"]);
        Assert.False((bool)caps["scrolller"]!["consent"]!);
        Assert.Null(caps["scrolller"]!["mediaOrigin"]);   // the page then takes its own (127.0.0.1) origin
        Assert.True((bool)caps["ownMedia"]!["library"]!);
        Assert.Null(caps["ownMedia"]!["mediaOrigin"]);
    });

    [Fact]
    public void TcMedia_ConsentIsAskedOnlyWhenItCanMatter() => RunTcMedia((_, w, sent, pools) =>
    {
        int asked = 0;
        w.TcAskConsent = () => { asked++; return Task.FromResult(false); };
        const string yes = "{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}";
        w.HandleMessage(yes);   // local source (the default): disabled at once, never asked (41c57f2de9)
        Assert.Equal(0, asked);
        Assert.Equal("disabled", (string?)LastOf(sent, "setting")["reason"]);
        CoreSettings.Current.MediaSource = "online";
        CoreSettings.Current.OfflineMode = true;
        w.HandleMessage(yes);
        Assert.Equal(0, asked);
        Assert.Equal("disabled", (string?)LastOf(sent, "setting")["reason"]);
        CoreSettings.Current.OfflineMode = false;
        w.HandleMessage(yes);
        Assert.Equal(1, asked);
        Assert.Equal("declined", (string?)LastOf(sent, "setting")["reason"]);
        Assert.False(CoreSettings.Current.RemoteMediaConsented);
        Assert.Empty(pools);
    });

    [Fact]
    public void TcMedia_YesStartsTheThemePool_FeedComesFromThisPage_OffAndPanicStopIt() => RunTcMedia((r, w, sent, pools) =>
    {
        CoreSettings.Current.MediaSource = "online";
        w.TcAskConsent = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"scrolller-niches\",\"niches\":[\"sissy\",\"nope\"]}");
        Assert.Empty(pools);   // nothing is fetched before consent
        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}");
        Assert.True(CoreSettings.Current.RemoteMediaConsented);
        var echo = LastOf(sent, "setting");
        Assert.True((bool)echo["value"]!);
        Assert.Null(echo["reason"]);
        var pool = Assert.Single(pools);
        Assert.Equal(TcMediaRules.ThemeSubs["sissy"], pool.Channels);
        var temp = Path.Combine(CorePaths.EffectiveAssets, ".temp");
        pool.Snapshot(new GoonOnlineMedia.Snapshot("ready", pool.Channels,
            new[] { new GoonOnlineMedia.Item("a", "x", Path.Combine(temp, "ccp_temp_remote_a.jpg")) },
            new[] { new GoonOnlineMedia.Item("b", "y", Path.Combine(temp, "ccp_temp_remote_b.mp4")) }, 2, 2));
        Dispatcher.UIThread.RunJobs();
        var rows = (JArray)LastOf(sent, "scrolller-feed")["rows"]!;
        Assert.Equal(new[] { "video", "image" }, rows.Select(x => (string)x["kind"]!));
        var url = new Uri((string)rows[1]["url"]!);
        Assert.Equal(w.PageUrl!.Authority, url.Authority);
        Assert.Equal("/ccp.assets/.temp/ccp_temp_remote_a.jpg", url.AbsolutePath);

        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":false}");   // this window only
        Assert.True(pool.Disposed);
        Assert.False(w.TcPicturesRunning);
        Assert.Null(LastOf(sent, "setting")["reason"]);
        Assert.Empty((JArray)LastOf(sent, "scrolller-feed")["rows"]!);
        Assert.True(CoreSettings.Current.RemoteMediaConsented);

        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}");
        var again = pools.Last();
        Assert.Equal(2, pools.Count);
        PanicSurfaces.StopAll("tc pictures panic test", r);
        Assert.True(again.Disposed);   // panic stops the fetch on the same call
        Assert.False(w.TcPicturesRunning);
        int before = sent.Count;
        again.Snapshot(new GoonOnlineMedia.Snapshot("ready", again.Channels,
            new[] { new GoonOnlineMedia.Item("a", "x", Path.Combine(temp, "ccp_temp_remote_a.jpg")) }, Array.Empty<GoonOnlineMedia.Item>(), 1, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, sent.Count);   // a late snapshot shows nothing
    });

    [Fact]
    public void TcMedia_PanicDuringTheConsentQuestionStartsNothing() => RunTcMedia((r, w, _, pools) =>
    {
        CoreSettings.Current.MediaSource = "online";
        var answer = new TaskCompletionSource<bool>();
        w.TcAskConsent = () => answer.Task;
        w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}");
        PanicSurfaces.StopAll("tc consent panic test", r);
        answer.SetResult(true);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(pools);
        Assert.False(CoreSettings.Current.RemoteMediaConsented);
    });

    [Fact]
    public void TcMedia_UseLocalFilesAnswersWithTheTickedLibrary() => RunTcMedia((_, w, sent, _) =>
    {
        var oldSources = GameWindow.TcLibrarySources;
        var dir = Path.Combine(CorePaths.EffectiveAssets, "images", "tc-own-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var pic = Path.Combine(dir, "a b.jpg");
            File.WriteAllBytes(pic, new byte[] { 1, 2, 3 });
            var outside = Path.GetTempFileName() + ".jpg";
            GameWindow.TcLibrarySources = () => new[]
            {
                new TcOwnMediaRules.Source(pic, false),
                new TcOwnMediaRules.Source(outside, false),   // no url outside the assets folder
                new TcOwnMediaRules.Source("https://example.com/x.jpg", false),
            };
            w.HandleMessage("{\"type\":\"own-media-list\"}");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!w.TcOwnMediaPending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            var frame = LastOf(sent, "own-media");
            Assert.False((bool)frame["asked"]!);
            var row = Assert.Single((JArray)frame["rows"]!);
            var url = new Uri((string)row["url"]!);
            Assert.Equal(w.PageUrl!.Authority, url.Authority);
            Assert.EndsWith("/a%20b.jpg", url.AbsolutePath);
            Assert.Equal("picture", (string?)row["kind"]);
            Assert.Equal(3, (long)row["size"]!);
        }
        finally
        {
            GameWindow.TcLibrarySources = oldSources;
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public void TcMedia_EndingTakesTheClaimPath_RefusedClientSideAsWpf() => RunTcMedia((_, w, sent, _) =>
    {
        var previous = SparklesHead.Service;
        var wire = new RewardWire();
        using var http = new HttpClient(wire);
        try
        {
            SparklesHead.Service = new SparklesClaimService(http, () => ("account", "test-token"), "http://localhost");
            var before = w.SparklesPending;
            w.HandleMessage("{\"type\":\"fuzz-ended\",\"ending\":\"compliant\",\"minutes\":12}");
            Assert.NotSame(before, w.SparklesPending);   // the claim went through the one Sparkles seam
            wire.Reply.TrySetResult(new(HttpStatusCode.OK) { Content = new StringContent("{ok:true}") });
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!w.SparklesPending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            // WPF at ed66bf9f44: SparklesClaim.IsGame has no "tc", so the claim is invalid and never sent (main b3d657ad80).
            Assert.Equal(0, wire.Calls);
            Assert.DoesNotContain(sent, o => (string?)o["type"] == "sparkles-receipt");
        }
        finally { SparklesHead.Service = previous; }
    });
}
