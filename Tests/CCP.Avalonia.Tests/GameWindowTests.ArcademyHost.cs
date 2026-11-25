using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LauncherCatalogue.cs:270-273 -> ArcademyHostService.Launch (:157-260), OnPageReady (:510-535),
/// meta-command (:594) and DisposeAll's meta flush: the Play card's door on this head.</summary>
public sealed partial class GameWindowTests
{
    private static MainShellWindow? _arcademyShell;

    private static void RunArcademy(bool lab, Action<List<string>, List<string>, string> body) => Run((shell, launcher) =>
    {
        _arcademyShell = shell;
        var (oldLab, oldDenied, oldNotice, oldMeta, oldAudio) = (CoreEntitlement.HasLabProvider, CoreEntitlement.ShowDeniedHandler,
            GameWindow.ArcademyNotice, GameWindow.NewArcademyMeta, CoreSettings.Current.AudioOnlySession);
        var metaPath = Path.Combine(Directory.CreateTempSubdirectory("arcademy-meta-").FullName, "arcademy_meta.json");
        var denied = new List<string>();
        var notices = new List<string>();
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => lab;
            CoreEntitlement.ShowDeniedHandler = v => denied.Add(v.Feature);
            GameWindow.ArcademyNotice = notices.Add;
            GameWindow.NewArcademyMeta = post => new ArcademyMetaStore(post, metaPath);
            launcher.Hide();
            body(denied, notices, metaPath);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            (CoreEntitlement.HasLabProvider, CoreEntitlement.ShowDeniedHandler, GameWindow.ArcademyNotice, GameWindow.NewArcademyMeta) =
                (oldLab, oldDenied, oldNotice, oldMeta);
            CoreSettings.Current.AudioOnlySession = oldAudio;
            Directory.Delete(Path.GetDirectoryName(metaPath)!, true);
            _arcademyShell = null;
        }
    });

    [Fact]
    public void Arcademy_Door_IsRegisteredBehindLabThenAudioOnly() => RunArcademy(lab: false, (denied, notices, _) =>
    {
        Assert.Equal("arcademy/index.html", GameWindow.Games["arcademy"].Page);
        Assert.Null(GameWindow.Launch("arcademy"));
        Assert.Equal(Loc.Get("launcher_game_arcademy_title"), Assert.Single(denied));
        Assert.Null(GameWindow.OpenForTests("arcademy"));

        CoreEntitlement.HasLabProvider = () => true;
        CoreSettings.Current.AudioOnlySession = true;
        Assert.Null(GameWindow.Launch("arcademy"));
        Assert.Equal(Loc.Get("arcademy_audio_only_refused"), Assert.Single(notices));
        Assert.Null(GameWindow.OpenForTests("arcademy"));

        CoreSettings.Current.AudioOnlySession = false;
        var w = GameWindow.Launch("arcademy");
        Assert.NotNull(w);
        Assert.Same(w, GameWindow.OpenForTests("arcademy"));
        Assert.Same(w, GameWindow.Launch("arcademy"));   // a live window is refocused, never relaunched
        Assert.Single(denied);
    });

    [Fact]
    public void Arcademy_Ready_PostsOneInitWithMetaAndMetaCommandsPersist() => RunArcademy(lab: true, (_, _, metaPath) =>
    {
        File.WriteAllText(metaPath, "{\"games\":{\"sort\":{\"tier\":2}}}");
        CoreSettings.Current.ArcademyAudioMute = true;
        var w = GameWindow.Launch("arcademy")!;
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        var init = Assert.Single(frames, f => (string?)f["type"] == "init");
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.Equal(2, (int)init["meta"]!["games"]!["sort"]!["tier"]!);
        Assert.True((bool)init["audioMute"]!);
        Assert.False((bool)init["remoteMediaEnabled"]!);
        Assert.Single(frames, f => (string?)f["type"] == "fullscreen");
        Assert.True(w.IsReady);

        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"seen\",\"value\":true}");
        Dispatcher.UIThread.RunJobs();
        var reply = frames.Last();
        Assert.Equal("meta", (string?)reply["type"]);
        Assert.True((bool)reply["value"]!);
        w.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True((bool)JObject.Parse(File.ReadAllText(metaPath))["seen"]!);
    });

    [Fact]
    public void Arcademy_Panic_ClosesWindowStopsWatchAndFlushesMeta() => RunArcademy(lab: true, (_, _, metaPath) =>
    {
        var w = GameWindow.Launch("arcademy")!;
        Assert.True(w.ArcademyWatchRunning);
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        w.HandleMessage("{\"type\":\"meta-command\",\"op\":\"set\",\"key\":\"seen\",\"value\":1}");
        var games = PanicSurfaces.All.Single(s => s.Id == "games");
        Assert.True(games.OwnsTheScreen!());
        games.Stop(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(GameWindow.OpenForTests("arcademy"));
        Assert.False(w.ArcademyWatchRunning);
        Assert.Equal(1, (int)JObject.Parse(File.ReadAllText(metaPath))["seen"]!);   // no debounce left running
    });
    [Fact]
    public void Arcademy_Window_InjectsTheCarrierForWebKit() => RunArcademy(lab: true, (_, _, _) =>
        Assert.True(GameWindow.Launch("arcademy")!.Web.InjectCarrier));   // arcademy/bridge.js reads only chrome.webview

    [Fact]
    public void Arcademy_ReadyInFront_KeepsEscapeAsItsPause() => RunArcademy(lab: true, (_, _, _) =>
    {
        var shell = _arcademyShell!;
        var w = GameWindow.Launch("arcademy")!;
        Assert.Equal(ConditioningControlPanel.Services.Safety.PanicPolicy.KeptEscapePage.Arcademy, w.KeptEscapePage);
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        w.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.TryGamePauseOnEscape(DateTime.UtcNow));
        Assert.Contains(frames, f => (string?)f["type"] == "kept-escape");
        Assert.Same(w, GameWindow.OpenForTests("arcademy"));
    });

    [Fact]
    public void Arcademy_ClassEnded_PaysXpOnceRecordsAttendanceAndPunches() => RunArcademy(lab: true, (_, _, metaPath) =>
    {
        var oldXp = CoreProgression.AddXPProvider;
        var xp = new List<(double, string)>();
        try
        {
            CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));
            var w = GameWindow.Launch("arcademy")!;
            var frames = new List<JObject>();
            w.Posted += json => frames.Add(JObject.Parse(json));
            const string ended = "{\"type\":\"class-ended\",\"gameKey\":\"sort\",\"gradeTier\":2,\"grade\":\"A\",\"flavorXp\":99,\"dareWon\":\"S\",\"dayUtc\":\"2026-01-02\"}";
            w.HandleMessage(ended);
            var pay = Assert.Single(frames, f => (string?)f["type"] == "payout-result");
            Assert.Equal(60 * 1.25 + 15, (double)pay["xp"]!);           // tier 2, A, flavour capped at 15
            Assert.Equal(15, (double)pay["dareXp"]!);
            Assert.Equal(1, (int)pay["streak"]!);
            Assert.False((bool)pay["retake"]!);
            Assert.Equal(new[] { (90.0, "Other"), (15.0, "Quest") }, xp);
            Assert.Single(frames, f => (string?)f["type"] == "punchcard-result");
            Assert.Contains(frames, f => (string?)f["type"] == "meta");

            frames.Clear();
            w.HandleMessage(ended);                                       // same UTC day: a free retake
            pay = Assert.Single(frames, f => (string?)f["type"] == "payout-result");
            Assert.True((bool)pay["retake"]!);
            Assert.Equal(0, (double)pay["xp"]!);
            Assert.Equal(2, xp.Count);
        }
        finally { CoreProgression.AddXPProvider = oldXp; }
    });

    [Fact]
    public void Arcademy_AudioOnlyFlippedWhileOpen_SuspendsThenResumes() => RunArcademy(lab: true, (_, _, _) =>
    {
        var w = GameWindow.Launch("arcademy")!;
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        CoreSettings.Current.AudioOnlySession = true;
        CoreSettings.Current.AudioOnlySession = false;
        Dispatcher.UIThread.RunJobs();
        var suspends = frames.Where(f => (string?)f["type"] == "suspend").ToList();
        Assert.Equal(new[] { true, false }, suspends.Select(f => (bool)f["on"]!));
        Assert.All(suspends, f => Assert.Equal("audio-only", (string?)f["reason"]));
        w.Close();
        Dispatcher.UIThread.RunJobs();
        frames.Clear();
        CoreSettings.Current.AudioOnlySession = true;                    // unhooked on close
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(frames);
    });
}
