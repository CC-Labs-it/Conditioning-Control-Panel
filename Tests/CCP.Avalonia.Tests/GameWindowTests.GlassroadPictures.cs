using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class PicturePool : GameWindow.IGlassroadPictures
    {
        public readonly Action<GoonOnlineMedia.Snapshot> Snapshot;
        public IReadOnlyList<string> Channels = Array.Empty<string>();
        public bool Disposed;
        public PicturePool(Action<GoonOnlineMedia.Snapshot> snapshot) => Snapshot = snapshot;
        public void Start(IReadOnlyList<string> channels) => Channels = channels;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void GlassroadPictures_ConsentNichesFeedOfflineAndPanic() => RunGlassroad((shell, _, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode);
        var (factory, oldHost) = (GameWindow.GlassroadPoolFactory, GlassroadHostRules.LinuxHostOverride);
        GlassroadHostRules.LinuxHostOverride = false;   // the WPF rule, whichever OS runs the suite (B2: Linux offers no Scrolller)
        var pools = new List<PicturePool>();
        try
        {
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = (null, "local", false, false, false);
            GameWindow.GlassroadPoolFactory = cb => { var p = new PicturePool(cb); pools.Add(p); return p; };
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            Dispatcher.UIThread.RunJobs();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostGlassroadInit();
            Assert.False((bool)sent.Last()["caps"]!["scrolller"]!["consent"]!);
            Assert.Equal("device", (string?)sent.Last()["caps"]!["scrolller"]!["origin"]);
            w.HandleMessage("{\"type\":\"scrolller-niches\",\"niches\":[\"hypno\",\"unknown\"]}");
            Assert.Empty(pools);
            w.SetGlassroadPicturesAsync(JObject.Parse("{\"key\":\"media.remoteConsent\",\"value\":true}")).GetAwaiter().GetResult();
            Assert.Empty(pools);
            w.GlassroadAskConsent = () => Task.FromResult(false);
            w.SetGlassroadPicturesAsync(JObject.Parse("{\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}")).GetAwaiter().GetResult();
            Assert.Empty(pools);
            Assert.False((bool)sent.Last()["value"]!);
            w.GlassroadAskConsent = () => Task.FromResult(true);
            int beforeAccept = sent.Count;
            w.SetGlassroadPicturesAsync(JObject.Parse("{\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}")).GetAwaiter().GetResult();
            var echo = Assert.Single(sent.Skip(beforeAccept).Where(o => (string?)o["type"] == "setting"));
            Assert.Equal("media.remoteConsent", (string?)echo["key"]);
            Assert.True((bool)echo["value"]!);
            var pool = Assert.Single(pools);
            Assert.Contains("EroticHypnosis", pool.Channels);
            Assert.DoesNotContain("unknown", pool.Channels);
            var path = Path.Combine(CorePaths.EffectiveAssets, ".temp", "ccp_temp_remote_test.jpg");
            pool.Snapshot(new GoonOnlineMedia.Snapshot("ready", pool.Channels,
                new[] { new GoonOnlineMedia.Item("one", "https://ccp.assets/.temp/ccp_temp_remote_test.jpg", path) },
                Array.Empty<GoonOnlineMedia.Item>(), 1, 1));
            Dispatcher.UIThread.RunJobs();
            var row = sent.Last(o => (string?)o["type"] == "scrolller-feed")["rows"]![0]!;
            var url = new Uri((string)row["url"]!);
            Assert.Equal(w.PageUrl!.Authority, url.Authority);
            Assert.Equal("/ccp.assets/.temp/ccp_temp_remote_test.jpg", url.AbsolutePath);
            Assert.Equal("image", (string?)row["kind"]);
            w.HandleMessage("{\"type\":\"scrolller-niches\",\"niches\":[\"sissy\"]}");
            Assert.True(pool.Disposed);
            Assert.Empty((JArray)sent.Last(o => (string?)o["type"] == "scrolller-feed")["rows"]!);
            var current = pools.Last();
            Assert.Contains("Sissies", current.Channels);
            int beforeOffline = sent.Count;
            s.OfflineMode = true;
            var offlineEcho = Assert.Single(sent.Skip(beforeOffline).Where(o => (string?)o["type"] == "setting"));
            Assert.False((bool)offlineEcho["value"]!);
            Assert.True(current.Disposed);
            Assert.Empty((JArray)sent.Last(o => (string?)o["type"] == "scrolller-feed")["rows"]!);
            pool.Snapshot(new GoonOnlineMedia.Snapshot("ready", pool.Channels,
                new[] { new GoonOnlineMedia.Item("one", "x", path) }, Array.Empty<GoonOnlineMedia.Item>(), 1, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty((JArray)sent.Last(o => (string?)o["type"] == "scrolller-feed")["rows"]!);
            int beforeOnline = sent.Count;
            s.OfflineMode = false;
            var onlineEcho = Assert.Single(sent.Skip(beforeOnline).Where(o => (string?)o["type"] == "setting"));
            Assert.True((bool)onlineEcho["value"]!);
            Assert.Equal(3, pools.Count);
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pools.Last().Disposed);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.GlassroadPoolFactory = factory;
            GlassroadHostRules.LinuxHostOverride = oldHost;
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = old;
        }
    });
    [Fact]
    public void GlassroadPictures_PanicInvalidatesPendingConsent() => RunGlassroad((shell, _, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode);
        var (factory, oldHost) = (GameWindow.GlassroadPoolFactory, GlassroadHostRules.LinuxHostOverride);
        GlassroadHostRules.LinuxHostOverride = false;   // the WPF rule, whichever OS runs the suite (B2: Linux offers no Scrolller)
        var pools = new List<PicturePool>();
        try
        {
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = (null, "local", false, false, false);
            GameWindow.GlassroadPoolFactory = cb => { var p = new PicturePool(cb); pools.Add(p); return p; };
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            Dispatcher.UIThread.RunJobs();
            var answer = new TaskCompletionSource<bool>();
            w.GlassroadAskConsent = () => answer.Task;
            var pending = w.SetGlassroadPicturesAsync(JObject.Parse("{\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}"));
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            answer.SetResult(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pending.IsCompleted);
            Assert.False(s.HasRemoteMediaConsent);
            Assert.Null(s.GlassroadPictures);
            Assert.Empty(pools);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.GlassroadPoolFactory = factory;
            GlassroadHostRules.LinuxHostOverride = oldHost;
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = old;
        }
    });

    /// <summary>B2 (owner 2026-10-29): on the Linux host the strongest Windows yes (own press, app consent, online
    /// source) still offers no Scrolller: the init caps say false, the page's niche pick and its own press start no
    /// pool and ask no consent, a settings flip starts nothing, no feed row ever leaves. The same settings on a
    /// Windows host start the pool (the WPF rule).</summary>
    [Fact]
    public void GlassroadPictures_LinuxHostNeverOffersOrStartsScrolller() => RunGlassroad((_, _, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode);
        var (factory, oldHost) = (GameWindow.GlassroadPoolFactory, GlassroadHostRules.LinuxHostOverride);
        var pools = new List<PicturePool>();
        try
        {
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = (true, "online", true, false, false);
            GlassroadHostRules.LinuxHostOverride = true;
            GameWindow.GlassroadPoolFactory = cb => { var p = new PicturePool(cb); pools.Add(p); return p; };
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            Dispatcher.UIThread.RunJobs();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostGlassroadInit();
            var caps = sent.Last(o => (string?)o["type"] == "init")["caps"]!;
            Assert.Equal(JTokenType.Boolean, caps["scrolller"]!.Type);
            Assert.False((bool)caps["scrolller"]!);
            w.HandleMessage("{\"type\":\"scrolller-niches\",\"niches\":[\"hypno\"]}");
            Assert.Empty(pools);
            // A fresh install's press would ask for the app consent: the Linux host asks nothing and writes nothing.
            s.RemoteMediaConsented = false;
            bool asked = false;
            w.GlassroadAskConsent = () => { asked = true; return Task.FromResult(true); };
            int before = sent.Count;
            w.SetGlassroadPicturesAsync(JObject.Parse("{\"key\":\"media.remoteConsent\",\"value\":true,\"ask\":true}")).GetAwaiter().GetResult();
            Assert.False(asked);
            Assert.False(s.HasRemoteMediaConsent);
            Assert.True(s.GlassroadPictures);
            var echo = Assert.Single(sent.Skip(before).Where(o => (string?)o["type"] == "setting"));
            Assert.False((bool)echo["value"]!);
            Assert.Empty(pools);
            // The settings path (an app-wide flip) starts nothing either.
            s.RemoteMediaConsented = true;
            s.OfflineMode = true;
            s.OfflineMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(pools);
            Assert.All(sent.Where(o => (string?)o["type"] == "setting"), o => Assert.False((bool)o["value"]!));
            Assert.All(sent.Where(o => (string?)o["type"] == "scrolller-feed"), o => Assert.Empty((JArray)o["rows"]!));
            // The same settings on a Windows host: the WPF rule holds and the pool starts.
            GlassroadHostRules.LinuxHostOverride = false;
            w.HandleMessage("{\"type\":\"scrolller-niches\",\"niches\":[\"hypno\"]}");
            Assert.Contains("EroticHypnosis", Assert.Single(pools).Channels);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.GlassroadPoolFactory = factory;
            GlassroadHostRules.LinuxHostOverride = oldHost;
            (s.GlassroadPictures, s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.OfflineMode) = old;
        }
    });
}
