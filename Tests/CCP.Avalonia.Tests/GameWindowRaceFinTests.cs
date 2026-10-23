using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Race_FullscreenReportAndGracefulPanic() => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        sent.Clear();
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.FullScreen, w.WindowState);
        Assert.True((bool)Assert.Single(sent)["on"]!);
        w.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(WindowState.Normal, w.WindowState);
        Assert.False((bool)sent.Last()["on"]!);
        w.HandleMessage("{\"type\":\"report-bug\"}");
        Dispatcher.UIThread.RunJobs();
        var report = Assert.Single(w.OwnedWindows.OfType<BugReportWindow>());
        Assert.Same(w, report.Owner);
        sent.Clear();
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(MainShellWindow.Current!);
        Assert.False(report.IsVisible);
        Assert.True(GameWindow.IsAnyOpen());
        Assert.Equal("exit-request", (string?)Assert.Single(sent)["type"]);
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Race_GracefulExitWatchdogClosesASilentPage() => RunRace("rt.original.03", (_, _, _) =>
    {
        var oldRunOnce = GameWindow.RaceRunOnce;
        var armed = new List<(Action Fire, TimeSpan Delay)>();
        GameWindow.RaceRunOnce = (fire, delay) => armed.Add((fire, delay));
        try
        {
            var w = GameWindow.Launch("race")!;
            w.HandleMessage("{\"type\":\"ready\"}");
            w.CloseRaceGracefully();
            Assert.True(GameWindow.IsAnyOpen());
            var watchdog = Assert.Single(armed);
            Assert.Equal(TimeSpan.FromMilliseconds(1200), watchdog.Delay);
            watchdog.Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.False(w.RaceWatchRunning);
        }
        finally { GameWindow.RaceRunOnce = oldRunOnce; }
    });

    [Fact]
    public void Race_FirstEscapePausesSecondEscapeRequestsExit() => RunRace("rt.original.03", (_, _, _) =>
    {
        var s = CoreSettings.Current;
        s.PanicKey = "Escape";
        s.PanicKeyEnabled = true;
        var w = GameWindow.Launch("race")!;
        w.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(w, GameWindow.RaceInFront());
        w.HandleMessage("{\"type\":\"ready\"}");
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        _raceShell!.HandlePanicKeyPress(now);
        Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
        Assert.True(GameWindow.IsAnyOpen());
        _raceShell.HandlePanicKeyPress(now.AddSeconds(1));
        Assert.Equal("exit-request", (string?)sent.Last()["type"]);
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Race_VideoPauseResumeAndClosedQueueAreSafe() => RunRace("rt.original.03", (_, _, _) =>
    {
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        GameWindow.PauseRacesForVideo(true);
        GameWindow.PauseRacesForVideo(false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { true, false }, sent.Select(o => (bool)o["on"]!));
        Assert.All(sent, o => Assert.Equal("pause", (string?)o["type"]));
        sent.Clear();
        GameWindow.PauseRacesForVideo(true);
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
    });

    [Fact]
    public void Race_ManifestIncludesVideosAndSpiralsRepostUntilClose() => RunRace("rt.original.03", (_, _, _) =>
    {
        var videos = Directory.CreateDirectory(Path.Combine(CorePaths.EffectiveAssets, "videos"));
        File.WriteAllBytes(Path.Combine(videos.FullName, "race.mp4"), new byte[] { 1 });
        var w = GameWindow.Launch("race")!;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        var video = Assert.Single(sent.Single(o => (string?)o["type"] == "manifest")["videos"]!);
        Assert.Equal("/ccp.assets/videos/race.mp4", (string?)video["url"]);
        sent.Clear();
        var gif = Convert.ToBase64String(new byte[] { 71, 73, 70, 56, 57, 97, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0x3b });
        try
        {
            Assert.True(DtrhLoomStore.Save("race-fin", gif, JObject.Parse("{\"speed\":2}"), true).Ok);
            Dispatcher.UIThread.RunJobs();
            var spiral = Assert.Single(sent.Last()["spirals"]!.Where(s => (string?)s["slug"] == "race-fin"));
            Assert.Equal("/ccp.spirals/loom_race-fin.gif", (string?)spiral["url"]);
            Assert.Equal(2, (int)spiral["params"]!["speed"]!);
            w.Close();
            sent.Clear();
            Assert.True(DtrhLoomStore.Delete("race-fin").Ok);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);
        }
        finally { DtrhLoomStore.Delete("race-fin"); }
    });

    [Fact]
    public void Race_FavoritesSfxAndAudioPayloadUseExistingStores() => RunRace("rt.original.03", (_, _, _) =>
    {
        // P02: the store is process-wide; snapshot its in-memory map (the sandbox json is restored by TestIsolation).
        var statsField = typeof(DtrhAssetStatsStore).GetField("_stats", BindingFlags.NonPublic | BindingFlags.Static)!;
        DtrhAssetStatsStore.TopAssets(0);
        var oldStats = (System.Collections.IDictionary)statsField.GetValue(null)!;
        var statsCopy = Activator.CreateInstance(oldStats.GetType(), oldStats, StringComparer.OrdinalIgnoreCase)!;
        DtrhAssetStatsStore.Merge(JObject.Parse("{\"stats\":[{\"name\":\"race-favorite.gif\",\"grabs\":100}]}"));
        var cue = Path.Combine(ContentLocator.ContentRoot, "Resources", "sounds", "chaos", "race_fin_test.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(cue)!);
        File.WriteAllBytes(cue, new byte[] { 1 });
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldShow = CoreSubliminal.ShowProvider;
        var plays = new List<(string, float)>();
        var phrases = new List<string>();
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.SubliminalPool.Clear();
        CoreSettings.Current.SubliminalPool["race phrase"] = true;
        CoreAudio.PlayOneShotProvider = (path, volume, _, _, _) => plays.Add((path, volume));
        CoreSubliminal.ShowProvider = phrases.Add;
        try
        {
            var w = GameWindow.Launch("race")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            Assert.Contains("race-favorite.gif", sent.Single(o => (string?)o["type"] == "favorites")["names"]!.Values<string>());
            w.HandleMessage("{\"type\":\"sfx\",\"name\":\"race_fin_test\"}");
            Assert.Equal((cue, 0.3f), Assert.Single(plays));
            w.HandleMessage("{\"type\":\"sfx\",\"name\":\"../race_fin_test\"}");
            Assert.Single(plays);
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"video\"}");
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"flash\"}");
            Assert.Empty(phrases);
            w.HandleMessage("{\"type\":\"fire-payload\",\"kind\":\"AuDiO\"}");
            Assert.Equal("race phrase", Assert.Single(phrases));
        }
        finally
        {
            CoreAudio.PlayOneShotProvider = oldAudio;
            CoreSubliminal.ShowProvider = oldShow;
            File.Delete(cue);
            statsField.SetValue(null, statsCopy);
        }
    });

    [Fact]
    public void Race_EngagementAndPayoutCreditOnceEvenWhenXpThrows() => RunRace("rt.original.03", (_, _, _) =>
    {
        var logProperty = typeof(Head).GetProperty("FeatureDayLog", BindingFlags.NonPublic | BindingFlags.Static)!;
        var achievementProperty = typeof(Head).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldLog = logProperty.GetValue(null);
        var oldAchievements = achievementProperty.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("race-credit-").FullName;
        using var log = new FeatureDayLogService(() => null, Path.Combine(dir, "days.json"));
        var achievements = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        int opens = EmiState.Current.Usage.GetValueOrDefault("race");
        logProperty.SetValue(null, log);
        achievementProperty.SetValue(null, achievements);
        try
        {
            var w = GameWindow.Launch("race")!;
            Assert.Equal(opens + 1, EmiState.Current.Usage["race"]);
            Assert.Same(w, GameWindow.Launch("race"));
            Assert.Equal(opens + 1, EmiState.Current.Usage["race"]);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            Assert.Equal(1, log.Log.Days.Sum(d => d.Get("e_rc")));
            CoreProgression.AddXPProvider = (_, _) => throw new InvalidOperationException("test");
            achievements.Progress.TotalBubblesPopped = 95;
            int points = CoreSettings.Current.SkillPoints;
            w.HandleMessage("{\"type\":\"run-ended\",\"popped\":255}");
            var popup = w.RaceMilestonePopup;
            Assert.NotNull(popup);
            Assert.True(popup.IsVisible);
            Assert.Equal(350, achievements.Progress.TotalBubblesPopped);
            Assert.Equal(points + 3, CoreSettings.Current.SkillPoints);
            w.HandleMessage("{\"type\":\"run-ended\",\"popped\":255}");
            Assert.Same(popup, w.RaceMilestonePopup);
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(MainShellWindow.Current!);
            Assert.False(popup.IsVisible);
            Assert.Single(sent.Where(o => (string?)o["type"] == "payout-result"));
        }
        finally
        {
            logProperty.SetValue(null, oldLog);
            achievementProperty.SetValue(null, oldAchievements);
            log.Dispose();
            Directory.Delete(dir, true);
        }
    });

    [Theory]
    [InlineData("run-ended")]
    [InlineData("exit")]
    public void Race_RunEndAndExitStopTheLocalTrack(string type) => RunRace("rt.original.03", (_, _, _) =>
    {
        WithTrackFiles((_, _) => Tone(4), files =>
        {
            var player = new FakeTrackPlayer();
            var w = GameWindow.Launch("race")!;
            w.RaceNewPlayer = () => player;
            w.RacePickFile = () => System.Threading.Tasks.Task.FromResult<string?>(files[0]);
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            w.HandleMessage("{\"type\":\"track-play\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"" + type + "\"}");
            Assert.Equal("Stop", player.Calls.Last());
            Assert.False(w.RaceTrackClockRunning);
        });
    });
}
