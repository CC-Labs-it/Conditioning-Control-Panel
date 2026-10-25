using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Race_SfxUsesMasterVolumeAndRejectsTraversal() => RunRace("rt.original.03", (_, _, _) =>
    {
        var cue = "race_test_" + Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(ContentLocator.InstallRoot, "Resources", "sounds", "chaos", cue + ".mp3");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[] { 1 });
        var old = global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
        var played = new List<float>();
        try
        {
            global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (_, volume, tag, _, _) =>
            {
                Assert.Equal("chaos-sfx", tag);
                played.Add(volume);
            };
            CoreSettings.Current.MasterVolume = 50;
            var w = GameWindow.Launch("race")!;
            w.HandleMessage(new JObject { ["type"] = "sfx", ["name"] = cue }.ToString());
            Assert.Equal(0.3f, Assert.Single(played), 4);
            w.HandleMessage(new JObject { ["type"] = "sfx", ["name"] = "../" + cue }.ToString());
            Assert.Single(played);
        }
        finally
        {
            global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = old;
            System.IO.File.Delete(path);
        }
    });

    [Fact]
    public void Race_BootPostsFavoritesAfterManifest() => RunRace("rt.original.03", (_, _, _) =>
    {
        // The store is process-global: keep this fixture from changing another boot test's offer.
        var field = typeof(ConditioningControlPanel.Services.Chaos.DtrhAssetStatsStore)
            .GetField("_stats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var path = System.IO.Path.Combine(CorePaths.UserData, "dtrh_asset_stats.json");
        var bytes = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        try
        {
            field.SetValue(null, null);
            System.IO.File.Delete(path);
            ConditioningControlPanel.Services.Chaos.DtrhAssetStatsStore.Merge(JObject.Parse(
                "{\"stats\":[{\"name\":\"race-favorite.gif\",\"weighted\":100}]}"));
            var w = GameWindow.Launch("race")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal(new[] { "init", "manifest", "favorites" }, sent.Select(f => (string?)f["type"]));
            Assert.Contains("race-favorite.gif", sent.Last()["names"]!.Values<string>());
        }
        finally
        {
            field.SetValue(null, previous);
            if (bytes == null) System.IO.File.Delete(path);
            else System.IO.File.WriteAllBytes(path, bytes);
        }
    });

    [Fact]
    public void Race_FullscreenEchoesBothStates() => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        foreach (var on in new[] { true, false })
        {
            w.HandleMessage(new JObject { ["type"] = "fullscreen-set", ["on"] = on }.ToString());
            Dispatcher.UIThread.RunJobs();
            var frame = Assert.Single(sent);
            Assert.Equal("fullscreen", (string?)frame["type"]);
            Assert.Equal(on, (bool)frame["on"]!);
            Assert.Equal(on ? WindowState.FullScreen : WindowState.Normal, w.WindowState);
            sent.Clear();
        }
    });

    [Fact]
    public void Race_ReportIsOwnedAndPanicClosesBoth() => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        w.HandleMessage("{\"type\":\"report-bug\"}");
        Dispatcher.UIThread.RunJobs();
        var report = Assert.IsType<BugReportWindow>(Assert.Single(w.OwnedWindows));
        Assert.True(report.IsVisible);
        GameWindow.CloseAllForPanic();
        Dispatcher.UIThread.RunJobs();
        Assert.False(report.IsVisible);
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Race_RunEndAndExitStopAudioImmediately() => RunRace("rt.original.03", (_, _, _) =>
    {
        WithTrackFiles((_, _) => Tone(30), files =>
        {
            var player = new FakeTrackPlayer();
            var w = GameWindow.Launch("race")!;
            w.RacePickFile = () => System.Threading.Tasks.Task.FromResult<string?>(files[0]);
            w.RaceNewPlayer = () => player;
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            w.HandleMessage("{\"type\":\"track-play\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.False(player.IsPlaying);
            Assert.False(w.RaceTrackClockRunning);
            w.HandleMessage("{\"type\":\"track-play\"}");
            w.HandleMessage("{\"type\":\"exit\"}");
            Assert.False(player.IsPlaying);
        });
    });

    [Fact]
    public void Race_EscapePausesThenPanics() => Run((shell, _) =>
    {
        var old = ConditioningControlPanel.Services.Race.RacingAccess.IsGrantedProvider;
        try
        {
            ConditioningControlPanel.Services.Race.RacingAccess.IsGrantedProvider = id => id == "rt.original.03";
            CoreSettings.Current.PanicKey = "Escape";
            CoreSettings.Current.PanicKeyEnabled = true;
            var w = GameWindow.Launch("race")!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            var now = new DateTime(2026, 1, 1);
            shell.HandlePanicKeyPress(now);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
            shell.HandlePanicKeyPress(now.AddSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { ConditioningControlPanel.Services.Race.RacingAccess.IsGrantedProvider = old; }
    });
}
