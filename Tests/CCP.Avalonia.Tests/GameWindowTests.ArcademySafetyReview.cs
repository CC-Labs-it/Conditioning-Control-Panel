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
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        try { w.Show(); Assert.False(w.IsReady); Assert.True(GameWindow.HandleArcademyPanic(DateTime.Now)); Assert.Null(GameWindow.OpenForTests("arcademy")); }
        finally { w.Close(); }
    });
    [Fact]
    public void Arcademy_OtherWindowCloseKeepsBothSyncStores() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]); var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
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
        var w = new GameWindow(GameWindow.Games["arcademy"]); string? reason = null;
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
        var w = new GameWindow(GameWindow.Games["arcademy"]); var frames = new List<JObject>();
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
    [Theory]
    [InlineData("samples")]
    [InlineData("settings")]
    [InlineData("browser")]
    public void Arcademy_ExternalSettingsAndBrowserPlaybackAreProjected(string focus) => Run((_, _) =>
    {
        var s = CoreSettings.Current; var old = (s.MasterVolume, s.ArcademyMasterIntensity, s.AudioOnlySession, s.ProtectBrowserVideoPlayback,
            SettingsTabView.InterruptingBrowserMedia, SettingsTabView.RemixMediaTick);
        var w = new GameWindow(GameWindow.Games["arcademy"]); var frames = new List<JObject>();
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
