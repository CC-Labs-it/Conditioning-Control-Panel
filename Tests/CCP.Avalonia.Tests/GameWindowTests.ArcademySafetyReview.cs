using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_BootPanicCannotBeLost() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.ArcademySpec);
        try { w.Show(); Assert.False(w.IsReady); Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now)); Assert.Null(GameWindow.OpenForTests("arcademy")); }
        finally { w.Close(); }
    });
    [Fact]
    public void Arcademy_OtherWindowCloseKeepsBothSyncStores() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.ArcademySpec); var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
        var cards = typeof(ArcademySyncService).GetField("_store", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wallet = typeof(ArcademyWalletSyncService).GetField("_store", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            w.Show(); other.Show(); var expected = cards.GetValue(null);
            Assert.NotNull(expected); Assert.Same(expected, wallet.GetValue(null));
            other.Close(); Assert.Same(expected, cards.GetValue(null)); Assert.Same(expected, wallet.GetValue(null));
        }
        finally { other.Close(); w.Close(); }
    });
    [Fact]
    public void Arcademy_CounterClosesBeforePanicHandoff() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current; var old = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll);
        var w = new GameWindow(GameWindow.ArcademySpec); string? reason = null;
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("F8", true, false);
            launcher.Hide(); w.Show(); w.HandleMessage("{\"type\":\"ready\"}");
            var counter = GameWindow.LaunchPrizeCounter(onClosed: r => reason = r);
            Assert.NotNull(counter); shell.HandlePanicKeyPress(DateTime.Now);
            Assert.Null(GameWindow.OpenForTests(GameWindow.PrizeCounterId));
            Assert.Equal("panic", reason); Assert.Same(w, GameWindow.OpenForTests("arcademy"));
        }
        finally { w.Close(); (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = old; }
    });
    [Fact]
    public void Arcademy_RemoteAssetsRequireConsentAndPreserveTags() => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.OfflineMode, s.RemoteMediaConsented, s.FypOnlineConsented, s.MediaSource);
        var w = new GameWindow(GameWindow.ArcademySpec); var frames = new List<JObject>();
        var pending = new TaskCompletionSource<List<FypAssetManifest.Entry>>(TaskCreationOptions.RunContinuationsAsynchronously); int calls = 0; w.Posted += json => frames.Add(JObject.Parse(json));
        w.ArcademyFetchForTests = (_, subs) => { calls++; Assert.Equal(new[] { "cats" }, subs); return pending.Task; };
        try
        {
            s.OfflineMode = true; s.RemoteMediaConsented = true; s.FypOnlineConsented = false; s.MediaSource = "online"; w.HandleMessage("{\"type\":\"ready\"}"); Assert.True((bool?)frames.Single(f => (string?)f["type"] == "init")["remoteMediaEnabled"]); s.OfflineMode = false; s.RemoteMediaConsented = false;
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"no\",\"subs\":[\"cats\"],\"tag\":\"target\"}");
            Assert.Equal(0, calls); Assert.True((bool?)frames.Last()["done"]);
            s.RemoteMediaConsented = true; s.MediaSource = "local"; w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"local\"}"); Assert.Equal(0, calls);
            s.MediaSource = "online"; w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"yes\",\"subs\":[\"cats\"],\"tag\":\"target\"}");
            pending.SetResult(new() { new FypAssetManifest.Entry { Id = "scrolller/cats/1", Url = "https://example.com/a.jpg", Type = "image", Folder = "r/cats" },
                new FypAssetManifest.Entry { Id = "scrolller/dogs/2", Url = "https://example.com/b.jpg", Type = "image", Folder = "r/dogs" } });
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return frames.Any(f => (string?)f["reqId"] == "yes" && (bool?)f["done"] == true); }, TimeSpan.FromSeconds(10)));
            var result = frames.Last(f => (string?)f["reqId"] == "yes"); Assert.Equal("target", (string?)result["tag"]); Assert.Single((JArray)result["urls"]!);
            Assert.Equal("r/cats", (string?)result["urls"]?[0]?["src"]); var late = new TaskCompletionSource<List<FypAssetManifest.Entry>>(TaskCreationOptions.RunContinuationsAsynchronously);
            w.ArcademyFetchForTests = (_, _) => late.Task;
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"revoked\"}");
            s.RemoteMediaConsented = false; late.SetResult(new() { new FypAssetManifest.Entry { Id = "scrolller/cats/3", Url = "https://example.com/c.jpg", Type = "image", Folder = "r/cats" } });
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return frames.Any(f => (string?)f["reqId"] == "revoked" && (bool?)f["done"] == true); }, TimeSpan.FromSeconds(10)));
            Assert.Empty((JArray)frames.Last(f => (string?)f["reqId"] == "revoked")["urls"]!);
        }
        finally { w.Close(); (s.OfflineMode, s.RemoteMediaConsented, s.FypOnlineConsented, s.MediaSource) = old; }
    });
    /// <summary>WPF ArcademyHostService.cs:4654-4663, 4934-4938: a batch's surplus rows serve the next ask of the same pile; new subs drop them.</summary>
    [Fact]
    public void Arcademy_RemoteAssetSurplusServesLaterRequests() => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource);
        var w = new GameWindow(GameWindow.ArcademySpec); var frames = new List<JObject>(); int calls = 0; string pile = "cats";
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.ArcademyFetchForTests = (_, _) => { calls++; return Task.FromResult(Enumerable.Range(1, 3).Select(i => new FypAssetManifest.Entry
            { Id = "scrolller/" + pile + "/" + i, Url = $"https://example.com/{pile}{i}.jpg", Type = "image", Folder = "r/" + pile }).ToList()); };
        JObject Final(string id)
        {
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return frames.Any(f => (string?)f["reqId"] == id && (bool?)f["done"] == true); }, TimeSpan.FromSeconds(10)));
            return frames.Last(f => (string?)f["reqId"] == id);
        }
        string[] Urls(JObject f) => ((JArray)f["urls"]!).Select(u => (string)u["url"]!).ToArray();
        try
        {
            (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = (false, true, "online");
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"a\",\"count\":1,\"subs\":[\"cats\"],\"tag\":\"target\"}");
            Assert.Equal(new[] { "https://example.com/cats1.jpg" }, Urls(Final("a")));
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"b\",\"count\":1,\"subs\":[\"cats\"],\"tag\":\"target\"}");
            var b = Final("b"); Assert.Equal(1, calls); Assert.Equal("r/cats", (string?)b["urls"]?[0]?["src"]); Assert.Equal("target", (string?)b["urls"]?[0]?["tag"]);
            Assert.Equal(new[] { "https://example.com/cats2.jpg" }, Urls(b));
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"u\",\"count\":1}");
            Assert.Equal(new[] { "https://example.com/cats1.jpg" }, Urls(Final("u"))); Assert.Equal(2, calls);   // the untagged pull keeps its own buffer
            pile = "dogs"; w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"d\",\"count\":1,\"subs\":[\"dogs\"],\"tag\":\"target\"}");
            Assert.Equal(new[] { "https://example.com/dogs1.jpg" }, Urls(Final("d"))); Assert.Equal(3, calls);   // new subs drop cats3
        }
        finally { w.Close(); (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = old; }
    });
    /// <summary>WPF ArcademyHostService.cs:4971-4981, 5034: a pile ask during its fetch waits and is served from that batch's surplus.</summary>
    [Fact]
    public void Arcademy_OverlappingPileRequestsShareOneFetch() => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource);
        var w = new GameWindow(GameWindow.ArcademySpec); var frames = new List<JObject>(); int calls = 0;
        var pending = new TaskCompletionSource<List<FypAssetManifest.Entry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.ArcademyFetchForTests = (_, _) => { calls++; return pending.Task; };
        try
        {
            (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = (false, true, "online");
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"a\",\"count\":1,\"subs\":[\"cats\"],\"tag\":\"target\"}");
            w.HandleMessage("{\"type\":\"assets-request\",\"reqId\":\"b\",\"count\":2,\"subs\":[\"cats\"],\"tag\":\"target\"}");
            Assert.DoesNotContain(frames, f => (string?)f["reqId"] == "b" && (bool?)f["done"] == true);
            pending.SetResult(Enumerable.Range(1, 3).Select(i => new FypAssetManifest.Entry
                { Id = "scrolller/cats/" + i, Url = $"https://example.com/cats{i}.jpg", Type = "image", Folder = "r/cats" }).ToList());
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return frames.Any(f => (string?)f["reqId"] == "b" && (bool?)f["done"] == true); }, TimeSpan.FromSeconds(10)));
            Assert.Equal(1, calls);
            Assert.Equal(new[] { "https://example.com/cats2.jpg", "https://example.com/cats3.jpg" },
                ((JArray)frames.Last(f => (string?)f["reqId"] == "b")["urls"]!).Select(u => (string)u["url"]!).ToArray());
        }
        finally { w.Close(); (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = old; }
    });
    /// <summary>WPF ArcademyHostService.cs:4971-4981, 5034: a waiter stays behind the fetch it queued on, and a fetch for new subs waits for the one in the air.</summary>
    [Fact]
    public void Arcademy_PileChangeKeepsWaitersWithTheirFetch() => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource);
        var w = new GameWindow(GameWindow.ArcademySpec); var frames = new List<JObject>();
        var pending = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<List<FypAssetManifest.Entry>>>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.ArcademyFetchForTests = (_, subs) => (pending[subs[0]] = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        void Land(string pile) => (SpinWait.SpinUntil(() => pending.ContainsKey(pile), TimeSpan.FromSeconds(10)) ? pending[pile] : throw new TimeoutException(pile)).SetResult(Enumerable.Range(1, 3).Select(i => new FypAssetManifest.Entry
            { Id = "scrolller/" + pile + "/" + i, Url = $"https://example.com/{pile}{i}.jpg", Type = "image", Folder = "r/" + pile }).ToList());
        string[] Final(string id)
        {
            Assert.True(SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return frames.Any(f => (string?)f["reqId"] == id && (bool?)f["done"] == true); }, TimeSpan.FromSeconds(10)));
            return ((JArray)frames.Last(f => (string?)f["reqId"] == id)["urls"]!).Select(u => (string)u["url"]!).ToArray();
        }
        void Ask(string id, string pile) => w.HandleMessage($"{{\"type\":\"assets-request\",\"reqId\":\"{id}\",\"count\":1,\"subs\":[\"{pile}\"],\"tag\":\"target\"}}");
        try
        {
            (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = (false, true, "online");
            Ask("a", "cats"); Ask("b", "cats");
            Assert.True(SpinWait.SpinUntil(() => pending.ContainsKey("cats"), TimeSpan.FromSeconds(10)));
            Ask("d", "dogs"); Ask("e", "dogs");
            // The pile's coordinator reads one shared sub list: the dogs fetch waits until the cats one has its rows (WPF :4971-4981).
            Assert.False(SpinWait.SpinUntil(() => pending.ContainsKey("dogs"), TimeSpan.FromMilliseconds(300)));
            Assert.DoesNotContain(frames, f => (string?)f["reqId"] is "b" or "e" && (bool?)f["done"] == true);
            Land("cats");
            Assert.Equal(new[] { "https://example.com/cats1.jpg" }, Final("a"));
            Assert.Equal(new[] { "https://example.com/cats2.jpg" }, Final("b"));
            Land("dogs");
            Assert.Equal(new[] { "https://example.com/dogs1.jpg" }, Final("d"));
            Assert.Equal(new[] { "https://example.com/dogs2.jpg" }, Final("e"));
            Ask("f", "dogs");   // the stale cats batch is not the dogs pile's stock
            Assert.Equal(new[] { "https://example.com/dogs3.jpg" }, Final("f"));
            Assert.Equal(2, pending.Count);
        }
        finally { w.Close(); (s.OfflineMode, s.RemoteMediaConsented, s.MediaSource) = old; }
    });
    [Theory]
    [InlineData("samples")]
    [InlineData("settings")]
    [InlineData("browser")]
    public void Arcademy_ExternalSettingsAndBrowserPlaybackAreProjected(string focus) => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.MasterVolume, s.ArcademyMasterIntensity, s.AudioOnlySession, s.ProtectBrowserVideoPlayback,
            SettingsTabView.InterruptingBrowserMedia, SettingsTabView.RemixMediaTick);
        var w = new GameWindow(GameWindow.ArcademySpec); var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            s.AudioOnlySession = false; s.ProtectBrowserVideoPlayback = true;
            SettingsTabView.InterruptingBrowserMedia = true; SettingsTabView.RemixMediaTick = System.Diagnostics.Stopwatch.GetTimestamp();
            w.Show(); w.HandleMessage("{\"type\":\"ready\"}");
            if (focus == "samples")
            {
                var init = frames.Single(f => (string?)f["type"] == "init"); Assert.Contains("bell", ((JArray)init["sfxSamples"]!).Values<string>()); return;
            }
            if (focus == "settings")
            {
                s.MasterVolume = 73; s.ArcademyMasterIntensity = .4; Dispatcher.UIThread.RunJobs();
                Assert.Contains(frames, f => (string?)f["key"] == "masterVolume" && (double?)f["value"] == .73);
                Assert.Contains(frames, f => (string?)f["key"] == "masterIntensity" && (double?)f["value"] == .4);
                frames.Clear(); CoreSettings.Service!.RestoreFrom(new ConditioningControlPanel.Models.AppSettings { MasterVolume = 52 }); Dispatcher.UIThread.RunJobs();
                Assert.Contains(frames, f => (string?)f["key"] == "masterVolume" && (double?)f["value"] == .52);
                frames.Clear(); CoreSettings.Current.MasterVolume = 61; Dispatcher.UIThread.RunJobs();
                Assert.Contains(frames, f => (string?)f["key"] == "masterVolume" && (double?)f["value"] == .61); return;
            }
            Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == true && (string?)f["reason"] == "video");
            Assert.True(w.ArcademyWatchRunning);
            SettingsTabView.InterruptingBrowserMedia = false; w.ArcademyPushSuspension();
            Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == false);
            SettingsTabView.InterruptingBrowserMedia = true; SettingsTabView.RemixMediaTick = System.Diagnostics.Stopwatch.GetTimestamp(); w.ArcademyPushSuspension();
            Assert.True(frames.Count(f => (string?)f["type"] == "suspend" && (bool?)f["on"] == true) >= 2);
            frames.Clear(); Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now)); w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}"); Assert.DoesNotContain(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == false);
            SettingsTabView.InterruptingBrowserMedia = false; w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}"); Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == false);
            frames.Clear(); SettingsTabView.InterruptingBrowserMedia = true; w.ArcademyPushSuspension(); Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == true);
            frames.Clear(); s.AudioOnlySession = true; SettingsTabView.InterruptingBrowserMedia = false; Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == false);
            s.AudioOnlySession = false; Dispatcher.UIThread.RunJobs(); Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == false);
        }
        finally { w.Close(); (s.MasterVolume, s.ArcademyMasterIntensity, s.AudioOnlySession, s.ProtectBrowserVideoPlayback,
            SettingsTabView.InterruptingBrowserMedia, SettingsTabView.RemixMediaTick) = old; }
    });
}
