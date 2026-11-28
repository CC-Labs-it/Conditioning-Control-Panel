using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonBoot_EarlyReadyWaitsForConfirmedPageAndSendsPostPurgeManifest() => Run((_, _) =>
    {
        Assert.True(GameWindow.GoonInboxIdle.Wait(TimeSpan.FromSeconds(10)));
        var lib = Directory.CreateTempSubdirectory("goon-media-").FullName;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var disabled = CoreSettings.Current.DisabledAssetPaths;
        var store = TransferInboxStore.Instance;
        try
        {
            Directory.CreateDirectory(Path.Combine(lib, "images"));
            Directory.CreateDirectory(Path.Combine(lib, "videos"));
            File.WriteAllBytes(Path.Combine(lib, "images", "keep.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "images", "off.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(lib, "videos", "clip.mp4"), new byte[] { 1 });
            CorePaths.EffectiveAssetsProvider = () => lib;
            CoreSettings.Current.DisabledAssetPaths = new() { "images/off.png" };
            var data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a3sAAAAASUVORK5CYII=");
            var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var id = Guid.NewGuid().ToString("N");
            Assert.Null(store.Begin(id, sha, "image/png", data.Length));
            Assert.Null(store.AppendChunk(id, 0, Convert.ToBase64String(data)));
            Assert.True(store.Commit(id).Ok);
            var oldFile = Path.Combine(store.RecvDir, sha + ".png");
            Assert.True(File.Exists(oldFile));

            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.NewScrolllerEngine()(true);
            void Start(Uri url)
            {
                var e = new WebViewNavigationStartingEventArgs { Request = url };
                w.Web.OnNavigationStarted(null, e);
                Assert.False(e.Cancel);
            }
            var page = w.PageUrl!;
            var wrong = new Uri(page, "/other.html");
            Start(wrong);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnNavigationCompleted(wrong);
            Assert.Empty(sent);
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Start(wrong); // a later navigation must discard the old document's ready
            w.Web.OnNavigationCompleted(wrong);
            Assert.Empty(sent);
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            w.Web.OnNavigationCompleted(page, success: false);
            w.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.Empty(sent);
            Start(page);
            w.Web.OnWebMessage("{\"type\":\"goon-recv-abort\",\"id\":\"x\"}");
            w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ready\\\"}\"");
            Assert.Empty(sent);
            Assert.False(w.IsReady);
            w.Web.OnNavigationCompleted(page);
            WaitGoonReplies(sent, 3);
            Assert.Equal(new[] { "init", "fullscreen", "manifest" }, sent.Select(x => (string?)x["type"]));
            Assert.True(w.IsReady);
            var manifest = sent[2];
            Assert.Equal("keep.png", (string?)Assert.Single((JArray)manifest["images"]!)["name"]);
            Assert.Contains("/ccp.assets/images/keep.png", (string?)manifest["images"]![0]!["url"]);
            Assert.Equal("clip.mp4", (string?)Assert.Single((JArray)manifest["videos"]!)["name"]);
            Assert.NotNull(manifest["skipped"]); Assert.NotNull(manifest["truncated"]);
            Assert.Empty((JArray)manifest["received"]!);
            Assert.False(File.Exists(oldFile));
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = oldAssets;
            CoreSettings.Current.DisabledAssetPaths = disabled;
            Directory.Delete(lib, true);
        }
    });
    [Fact]
    public void GoonBoot_ConfirmedReadySendsManifest() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        WaitGoonReplies(sent, 3);
        Assert.Equal(new[] { "init", "fullscreen", "manifest" }, sent.Select(x => (string?)x["type"]));
        Assert.IsType<JArray>(sent[2]["received"]);
    });

    [Fact]
    public void Goon_OpenPrimeReachesThePremiumGate() => Run((_, _) =>
    {
        var (oldPremium, oldDenied) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler);
        var denied = new List<string>();
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = v => denied.Add(v.Feature);
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show();
            w.HandleMessage("{\"type\":\"open-prime\"}");
            Assert.Equal(new[] { Loc.Get("launcher_game_goon_title") }, denied);
            w.Close();
        }
        finally { (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldPremium, oldDenied); }
    });
}
