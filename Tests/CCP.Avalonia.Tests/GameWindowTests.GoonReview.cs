using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonBrainDrain_AlwaysAdvertisesThePageVeil() => Run((_, launcher) =>
    {
        launcher.Hide();
        var w = GameWindow.Launch("goon")!;
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.True((bool?)Assert.Single(frames, x => (string?)x["type"] == "init")["caps"]?["brainDrain"]);
        w.Close();
    });

    [Fact]
    public void GoonTransfer_PaidAccountCanSend() => Run((_, launcher) =>
    {
        var old = CoreAccount.HasPremiumAccessProvider;
        try
        {
            CoreAccount.HasPremiumAccessProvider = () => true;
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.True((bool?)Assert.Single(frames, x => (string?)x["type"] == "init")["caps"]?["mediaTransfer"]);
            w.Close();
        }
        finally { CoreAccount.HasPremiumAccessProvider = old; }
    });

    [Fact]
    public void GoonFullscreen_EchoesPersistsAndRestores() => Run((_, launcher) =>
    {
        var s = CoreSettings.Current;
        var old = s.GoonFullscreen;
        try
        {
            s.GoonFullscreen = false;
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
            Assert.Equal(WindowState.FullScreen, w.WindowState);
            Assert.True(s.GoonFullscreen);
            Assert.True((bool?)Assert.Single(frames, x => (string?)x["type"] == "fullscreen")["on"]);
            w.Close();
            var next = GameWindow.Launch("goon")!;
            Assert.Equal(WindowState.FullScreen, next.WindowState);
            frames.Clear();
            next.Posted += json => frames.Add(JObject.Parse(json));
            next.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
            Assert.NotEqual(WindowState.FullScreen, next.WindowState);
            Assert.False(s.GoonFullscreen);
            Assert.False((bool?)Assert.Single(frames, x => (string?)x["type"] == "fullscreen")["on"]);
            next.Close();
        }
        finally { s.GoonFullscreen = old; }
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
            var w = GameWindow.Launch("goon")!;
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
        using var server = new WebAssetServer(root) { GoonCacheRoot = () => cache };
        try
        {
            launcher.Hide();
            var w = new GameWindow(GameWindow.Games["goon"]);
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
            Assert.Null(server.ResolveFile("/__goon_cache__/art/index.json"));
            w.Close();
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public void GoonToys_ConnectCueAndCloseUseTheHapticService() => Run((_, launcher) =>
    {
        var settings = CoreSettings.Current.Haptics;
        settings.EnsureV2Migrated();
        var old = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider,
            ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast, settings.Enabled,
            settings.V2.Provider("mock").Enabled, settings.V2.Provider("buttplug").Enabled,
            settings.V2.Provider("lovense").Enabled);
        var toasts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        settings.Enabled = true;
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("lovense").Enabled = false;
        CoreEntitlement.HasPremiumProvider = () => true;
        ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = toasts.Enqueue;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        CoreHaptics.Service = haptics;
        try
        {
            launcher.Hide();
            var w = GameWindow.Launch("goon")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.False((bool?)Assert.Single(frames, x => (string?)x["type"] == "init")["caps"]?["haptics"]);
            Assert.True(haptics.ConnectAsync().GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(frames, x => (string?)x["type"] == "haptics-state" && (bool?)x["connected"] == true);
            toasts.Clear();
            w.HandleMessage("{\"type\":\"toy-pattern\",\"intensity\":0.4,\"cap\":0.5,\"durationMs\":3000,\"pattern\":\"pulse\"}");
            Assert.True(SpinWait.SpinUntil(() => toasts.Any(t => t.Contains('%')), TimeSpan.FromSeconds(10)));
            var playing = typeof(GoonToyDirector).GetField("_playing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var first = Assert.IsType<CancellationTokenSource>(playing.GetValue(null)).Token;
            w.HandleMessage("{\"type\":\"toy-stop\"}");
            Assert.True(first.IsCancellationRequested);
            toasts.Clear();
            w.HandleMessage("{\"type\":\"toy-pattern\",\"intensity\":0.4,\"cap\":0.5,\"durationMs\":3000,\"pattern\":\"pulse\"}");
            Assert.True(SpinWait.SpinUntil(() => playing.GetValue(null) is CancellationTokenSource, TimeSpan.FromSeconds(10)));
            var second = Assert.IsType<CancellationTokenSource>(playing.GetValue(null)).Token;
            Assert.True(SpinWait.SpinUntil(() => toasts.Any(t => t.Contains('%')), TimeSpan.FromSeconds(10)));
            w.Close();
            Assert.True(second.IsCancellationRequested);
        }
        finally
        {
            haptics.PanicStop();
            CoreHaptics.Service = old.Service;
            CoreEntitlement.HasPremiumProvider = old.HasPremiumProvider;
            ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = old.Toast;
            settings.Enabled = old.Item4;
            settings.V2.Provider("mock").Enabled = old.Item5;
            settings.V2.Provider("buttplug").Enabled = old.Item6;
            settings.V2.Provider("lovense").Enabled = old.Item7;
        }
    });

    [Fact]
    public void GoonToys_CuesAreClampedAndMapped()
    {
        var cue = GoonToyDirector.Parse(JObject.Parse("{\"intensity\":0.9,\"cap\":0.4,\"durationMs\":3,\"pattern\":\"wave\"}"));
        Assert.NotNull(cue);
        Assert.Equal(0.4, cue.Value.Level, 3);
        Assert.Equal(GoonToyDirector.MinMs, cue.Value.Ms);
        Assert.Equal(ConditioningControlPanel.Models.VibrationMode.Wave, cue.Value.Mode);
        Assert.Null(GoonToyDirector.Parse(JObject.Parse("{\"intensity\":1,\"cap\":0}")));
        Assert.False(GoonToyDirector.Ready(true, false));
        Assert.True(GoonToyDirector.Ready(true, true));
    }
}
