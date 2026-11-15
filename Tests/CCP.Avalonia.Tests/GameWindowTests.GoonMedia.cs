using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services.GoonGame;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private static void RunGoonMedia(Action<MainShellWindow, LauncherWindow> body)
    {
        var materialize = GoonOnlineMedia.Materialize;
        var release = GoonOnlineMedia.ReleaseTempFile;
        try { Run(body); }
        finally
        {
            GoonOnlineMedia.Materialize = materialize;
            GoonOnlineMedia.ReleaseTempFile = release;
        }
    }

    [Fact]
    public void GoonMedia_ExitDoneClosesImmediatelyAndDropsMediaDuringExit() => RunGoonMedia((shell, launcher) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var starts = 0;
        w.GoonStartForTests = (_, _) => starts++;
        CoreSettings.Current.GoonMediaOnline = true;
        w.HandleMessage("{\"type\":\"exit\"}");
        Assert.Contains(w, GameWindow.OpenWindowsForTests());
        w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"pink\",\"subs\":[\"good\"]}");
        Assert.Equal(0, starts);
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
    });

    [Fact]
    public void GoonMedia_RunnerRestoresProcessWideMediaDelegates()
    {
        var originalMaterialize = GoonOnlineMedia.Materialize;
        var originalRelease = GoonOnlineMedia.ReleaseTempFile;
        Func<string, CancellationToken, Task<string?>> sentinelMaterialize = (_, _) => Task.FromResult<string?>(null);
        Action<string> sentinelRelease = _ => { };
        try
        {
            GoonOnlineMedia.Materialize = sentinelMaterialize;
            GoonOnlineMedia.ReleaseTempFile = sentinelRelease;
            RunGoonMedia((shell, launcher) =>
            {
                var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
                w.Load(GameWindow.Server()); w.Show();
                CoreSettings.Current.GoonMediaOnline = false;
                w.HandleMessage("{\"type\":\"peer-niches\",\"subs\":[\"good\"]}");
                Assert.NotSame(sentinelMaterialize, GoonOnlineMedia.Materialize);
                Assert.NotSame(sentinelRelease, GoonOnlineMedia.ReleaseTempFile);
                w.Close();
            });
            Assert.Same(sentinelMaterialize, GoonOnlineMedia.Materialize);
            Assert.Same(sentinelRelease, GoonOnlineMedia.ReleaseTempFile);
        }
        finally
        {
            GoonOnlineMedia.Materialize = originalMaterialize;
            GoonOnlineMedia.ReleaseTempFile = originalRelease;
        }
    }

    [Fact]
    public void GoonMedia_RemembersPickWithoutSessionConsentAndCleansNiches() => RunGoonMedia((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        s.GoonMediaFlavour = "pink";
        s.GoonMediaOnline = false;
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var init = JObject.FromObject(w.GoonMediaBlock());
        Assert.Equal("", (string?)init["flavour"]);
        Assert.Equal("pink", (string?)init["last"]);
        w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"pink\",\"online\":false,\"subs\":[\"Good_one\",\"../bad\",7]}");
        Assert.Equal("Good_one", s.GoonMediaSubs);
        var frames = new List<string>(); w.Posted += frames.Add;
        w.HandleMessage("{\"type\":\"media-more\"}");
        Assert.Empty(frames);
        w.Close();
    });

    [Fact]
    public void GoonMedia_RefillRequiresSessionPickAndUsesCurrentSettings() => RunGoonMedia((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        s.GoonMediaOnline = true; s.GoonMediaFlavour = "pink"; s.GoonMediaSubs = "good";
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var starts = 0; var refills = 0;
        w.GoonStartForTests = (_, _) => starts++;
        w.GoonMoreForTests = () => refills++;
        w.HandleMessage("{\"type\":\"media-more\"}");
        Assert.Equal(0, refills);
        w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"pink\",\"online\":true,\"subs\":[\"good\"]}");
        w.HandleMessage("{\"type\":\"media-more\"}");
        Assert.Equal(1, starts); Assert.Equal(1, refills);
        s.GoonMediaOnline = false;
        w.HandleMessage("{\"type\":\"media-more\"}");
        Assert.Equal(1, refills);
        w.Close();
    });

    [Fact]
    public void GoonMedia_PeerAndNoiseUseOnlySanitizedNamesAndKnownBoards() => RunGoonMedia((shell, launcher) =>
    {
        var s = CoreSettings.Current; s.GoonMediaOnline = true;
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var starts = new Dictionary<string, string[]>();
        w.GoonStartForTests = (kind, subs) => starts[kind] = subs.ToArray();
        w.HandleMessage("{\"type\":\"media-flavour\",\"flavour\":\"pink\",\"online\":true,\"subs\":[]}");
        w.HandleMessage("{\"type\":\"peer-niches\",\"subs\":[\"Good_one\",\"../bad\",\"good_ONE\",8]}");
        w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"cats\"}");
        w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"../bad\"}");
        Assert.Equal(new[] { "Good_one" }, starts["peer"]);
        Assert.Equal(new[] { "cats" }, starts["cats"]);
        Assert.DoesNotContain("../bad", starts.Keys);
        w.Close();
    });

    [Fact]
    public void GoonMedia_MapsAssetsAndDropsOutdatedSnapshots() => RunGoonMedia((shell, launcher) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        var path = Path.Combine(CorePaths.EffectiveAssets, ".temp", "goon test.jpg");
        var item = new GoonOnlineMedia.Item("picture", "https://ccp.assets/.temp/goon%20test.jpg", path);
        var snap = new GoonOnlineMedia.Snapshot("ready", new[] { "good" }, new[] { item }, Array.Empty<GoonOnlineMedia.Item>(), 1, 1);
        w.PostGoonSnapshot("online-media", snap); Dispatcher.UIThread.RunJobs();
        var url = (string?)sent.Single(o => (string?)o["type"] == "online-media")["images"]![0]!["url"];
        Assert.StartsWith("http://127.0.0.1:", url);
        Assert.EndsWith("/ccp.assets/.temp/goon%20test.jpg", url);
        sent.Clear(); w.PostGoonSnapshot("online-media", snap);
        w.HandleMessage("{\"type\":\"media-flavour\",\"online\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(sent, o => (string?)o["state"] == "ready");
        w.Close();
    });

    [Fact]
    public void GoonMedia_DeclinesPeerAndKnownNoiseWhenOnlineIsOff() => RunGoonMedia((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        s.GoonMediaOnline = false;
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var frames = new List<string>(); w.Posted += frames.Add;
        w.HandleMessage("{\"type\":\"peer-niches\",\"subs\":[\"good\",\"../bad\"]}");
        w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"cats\"}");
        w.HandleMessage("{\"type\":\"noise-want\",\"set\":\"../bad\"}");
        Assert.Contains(frames, f => f.Contains("\"peer-media\"") && f.Contains("\"declined\""));
        Assert.Single(frames.Where(f => f.Contains("\"noise-media\"") && f.Contains("\"declined\"")));
        w.Close();
    });
}
