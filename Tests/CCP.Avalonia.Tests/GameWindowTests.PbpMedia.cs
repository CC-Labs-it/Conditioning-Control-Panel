using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    [Fact]
    public void PieceByPiece_MediaChoiceCleansPersistsAndNeverGrantsAppConsent() => RunBoard(() =>
    {
        var s = CoreSettings.Current;
        s.PbpMediaOnline = true; s.PbpMediaFlavour = ""; s.PbpMediaChosen = false;
        s.MediaSource = "local"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        Assert.True(w.PbpLocalProbe.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        w.HandleMessage("{\"type\":\"ready\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:media-state", "pbp:online-media" }, sent.Select(o => (string?)o["type"]));
        var state = sent.Single(o => (string?)o["type"] == "pbp:media-state");
        Assert.False((bool)state["chosen"]!);
        w.HandleMessage("{\"type\":\"pbp:media-flavour\",\"flavour\":\" PINK \",\"subs\":[\"r/valid\",\"VALID\",\"../bad\",3],\"custom\":{\"pink\":{}},\"online\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("pink", s.PbpMediaFlavour); Assert.Equal("valid", s.PbpMediaSubs);
        Assert.Equal("{\"pink\":{}}", s.PbpMediaCustom);
        Assert.True(s.PbpMediaChosen); Assert.False(s.HasRemoteMediaConsent);
        Assert.False((bool)sent.Last(o => (string?)o["type"] == "pbp:media-state")["mediaAvailable"]!); // dry local + own-only
        w.Close();
        w = GameWindow.Launch(GameWindow.PbpId)!;
        sent.Clear(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}");
        state = sent.Single(o => (string?)o["type"] == "pbp:media-state");
        Assert.True((bool)state["chosen"]!); Assert.False((bool)state["online"]!);
        Assert.Equal("pink", (string?)state["last"]); Assert.Equal("", (string?)state["flavour"]);
    });

    [Fact]
    public void PieceByPiece_MediaSnapshotsMapLoopbackRefreshDryAndDropLateReplies() => RunBoard(() =>
    {
        var s = CoreSettings.Current;
        s.PbpMediaOnline = true; s.PbpMediaFlavour = "pink"; s.PbpMediaSubs = "";
        s.MediaSource = "local"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("pink", (string?)sent.First(o => (string?)o["type"] == "pbp:media-state")["flavour"]);
        Assert.True(w.PbpLocalProbe.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        var path = Path.Combine(CorePaths.EffectiveAssets, ".temp", "pbp test.jpg");
        var item = new GoonOnlineMedia.Item("test", "https://ccp.assets/.temp/pbp%20test.jpg", path);
        var snap = new GoonOnlineMedia.Snapshot("ready", new[] { "valid" }, new[] { item }, Array.Empty<GoonOnlineMedia.Item>(), 1, 36);
        w.PostPbpOnline(snap);
        Dispatcher.UIThread.RunJobs();
        var frame = sent.Last(o => (string?)o["type"] == "pbp:online-media");
        Assert.StartsWith("http://127.0.0.1:", (string)frame["images"]![0]!);
        Assert.EndsWith("/ccp.assets/.temp/pbp%20test.jpg", (string)frame["images"]![0]!);
        Assert.Equal(70, (int)frame["share"]!); Assert.Equal(1, (int)frame["have"]!);
        w.PostPbpOnline(snap); // queued before switching off: must never resurrect the old deck
        w.HandleMessage("{\"type\":\"pbp:media-flavour\",\"online\":false}");
        sent.Clear(); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(sent, o => (string?)o["state"] == "ready");
        Assert.Equal("off", (string?)sent.Single(o => (string?)o["type"] == "pbp:online-media")["state"]);
        w.PostPbpOnline(snap); w.Close(); sent.Clear(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
    });
    [Fact]
    public void PieceByPiece_MediaSavedConsentStartsPoolAndOwnOnlyAndCloseStopIt() => RunBoard(() =>
    {
        ScrolllerPolicy.Apply(false, null); // no network in this test
        var s = CoreSettings.Current;
        s.PbpMediaOnline = true; s.PbpMediaFlavour = "pink"; s.PbpMediaSubs = "valid";
        s.MediaSource = "local"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}"); Dispatcher.UIThread.RunJobs();
        var pool = (GoonOnlineMedia)typeof(GameWindow).GetField("_pbpOnline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(w)!;
        Assert.NotNull(pool); Assert.NotNull(GoonOnlineMedia.Materialize); Assert.NotNull(GoonOnlineMedia.ReleaseTempFile);
        Assert.Contains(sent, o => (string?)o["state"] == "loading" && (int)o["share"]! == 70);
        w.HandleMessage("{\"type\":\"pbp:media-more\"}"); // running wave: bounded pool ignores refill
        w.HandleMessage("{\"type\":\"pbp:media-flavour\",\"online\":false}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("off", (string?)sent.Last(o => (string?)o["type"] == "pbp:online-media")["state"]);
        Assert.False(pool.More());
        w.Close(); Assert.False(pool.More());
    });

    [Fact]
    public void PieceByPiece_MediaProbeStartsOptimisticThenRefreshesEmptyLibrary() => RunBoard(() =>
    {
        CoreSettings.Current.PbpMediaOnline = false;
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.True((bool)sent[0]["mediaAvailable"]!);
        Assert.True(w.PbpLocalProbe.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        var states = sent.Where(o => (string?)o["type"] == "pbp:media-state").ToArray();
        Assert.Equal(2, states.Length);
        Assert.True((bool)states[0]["mediaAvailable"]!); Assert.False((bool)states[1]["mediaAvailable"]!);
        var count = sent.Count; Dispatcher.UIThread.RunJobs(); Assert.Equal(count, sent.Count);
    });

}
