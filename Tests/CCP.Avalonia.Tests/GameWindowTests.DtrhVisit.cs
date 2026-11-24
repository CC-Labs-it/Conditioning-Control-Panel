using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// game-dtrh: visit hooks, audio pack, haptics feed and the locked mandatory video (WPF DtrhHostService.cs:146-153,386-396,1036-1046).
public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_OpenNotesUsageAsksAudioAndEmiHearsOpenAndClose() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var (oldPack, oldUsage, oldMoment) = (GameWindow.RaceRequestPack, GameWindow.RaceNoteUsage, EmiTargets.Moment);
        var packs = new List<string>();
        var usage = new List<string>();
        var moments = new List<string>();
        try
        {
            GameWindow.RaceRequestPack = packs.Add;
            GameWindow.RaceNoteUsage = usage.Add;
            EmiTargets.Moment = (m, ctx) => moments.Add(ctx == null ? m : m + ":" + JObject.FromObject(ctx)["minutes"]);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            GameWindow.DtrhUtcNow = () => t0;
            var w = GameWindow.Launch("dtrh")!;
            Assert.Equal(new[] { "audio-web" }, packs);
            Assert.Equal(new[] { "dtrh" }, usage);
            Assert.Equal(new[] { "dtrhOpened" }, moments);
            GameWindow.DtrhUtcNow = () => t0.AddMinutes(7);
            w.Close();
            Assert.Equal(new[] { "dtrhOpened", "dtrhClosed:7" }, moments);
        }
        finally { GameWindow.RaceRequestPack = oldPack; GameWindow.RaceNoteUsage = oldUsage; EmiTargets.Moment = oldMoment; }
    });

    [Fact]
    public void Dtrh_HapticStateAndEveryBarkReachTheDirector() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var (oldState, oldEvent) = (GameWindow.DtrhHapticState, GameWindow.DtrhHapticEvent);
        var states = new List<double>();
        var events = new List<string?>();
        try
        {
            GameWindow.DtrhHapticState = o => states.Add((double)o["depth"]!);
            GameWindow.DtrhHapticEvent = o => events.Add((string?)o["event"]);
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{type:'ready'}");
            w.HandleMessage("{type:'haptic-state',running:true,depth:0.4,melt:0.1}");
            w.HandleMessage("{type:'bark',event:'combo-big'}");
            w.HandleMessage("{type:'bark',event:'effect-fired',kind:'flash'}");
            Assert.Equal(new[] { 0.4 }, states);
            Assert.Equal(new[] { "combo-big", "effect-fired" }, events);
            w.Close();
        }
        finally { GameWindow.DtrhHapticState = oldState; GameWindow.DtrhHapticEvent = oldEvent; }
    });

    // WPF DtrhHostService.cs:226,379,606,870,909,1071: the real director follows the window, the run and a covering video.
    [Fact]
    public void Dtrh_HapticDirectorFollowsOpenRunVideoAndClose() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var oldVideo = ConditioningControlPanel.CoreEngine.Video;
        var video = new ConditioningControlPanel.Services.MandatoryVideoScheduler(null!);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var started = typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("VideoStarted", flags)!;
        var ended = typeof(ConditioningControlPanel.Services.MandatoryVideoScheduler).GetField("VideoEnded", flags)!;
        try
        {
            ConditioningControlPanel.CoreEngine.Video = video;
            var w = GameWindow.Launch("dtrh")!;
            Assert.True(DtrhHapticDirector.Snapshot.Active);
            Assert.False(DtrhHapticDirector.Snapshot.RunActive);
            w.HandleMessage("{type:'ready'}");
            w.HandleMessage("{type:'run-started'}");
            Assert.True(DtrhHapticDirector.Snapshot.RunActive);
            Assert.IsType<Action>(started.GetValue(video)).Invoke();
            Assert.True(DtrhHapticDirector.Snapshot.VideoCovering);
            Assert.IsType<Action>(ended.GetValue(video)).Invoke();
            Assert.False(DtrhHapticDirector.Snapshot.VideoCovering);
            // WPF :346,607,817: the in-world Freeze holds the toy, lifts with the bubble or the run's end.
            w.HandleMessage("{type:'freeze-state',on:true}");
            Assert.True(DtrhHapticDirector.Snapshot.WorldFrozen);
            w.HandleMessage("{type:'freeze-state',on:false}");
            Assert.False(DtrhHapticDirector.Snapshot.WorldFrozen);
            w.HandleMessage("{type:'freeze-state',on:true}");
            w.HandleMessage("{type:'run-ended',abandoned:true}");
            Assert.False(DtrhHapticDirector.Snapshot.WorldFrozen);
            Assert.False(DtrhHapticDirector.Snapshot.RunActive);
            Assert.True(DtrhHapticDirector.Snapshot.Active);
            w.Close();
            Assert.False(DtrhHapticDirector.Snapshot.Active);
            Assert.Null(started.GetValue(video));
            Assert.Null(ended.GetValue(video));
        }
        finally
        {
            if (DtrhHapticDirector.Snapshot.Active) DtrhHapticDirector.OnClosed();
            ConditioningControlPanel.CoreEngine.Video = oldVideo;
        }
    });

    [Fact]
    public void Dtrh_RunningDescentLocksMandatoryVideo() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var oldFloat = ConditioningControlPanel.CoreSettings.Current.MandatoryVideoFloating;
        try
        {
            ConditioningControlPanel.CoreSettings.Current.MandatoryVideoFloating = true;
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{type:'ready'}");
            Assert.False(MandatoryVideoOverlay.LockedRun);
            Assert.True(MandatoryVideoOverlay.Floats(false));
            w.HandleMessage("{type:'run-started'}");
            Assert.True(MandatoryVideoOverlay.LockedRun);
            Assert.False(MandatoryVideoOverlay.Floats(false));
            w.Close();
            Assert.False(MandatoryVideoOverlay.LockedRun);
        }
        finally { ConditioningControlPanel.CoreSettings.Current.MandatoryVideoFloating = oldFloat; }
    });

    // Review P1: a panic must not leave a run-end farewell wave to play after the mixer's 400 ms mute.
    // Panic then the 1,200 ms watchdog (teardown), and panic then a page that settles, both end without it.
    [Fact]
    public void Dtrh_PanicEndsTheRunWithoutAFarewellWave() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var old = GameWindow.DtrhHapticRunEnded;
        var farewells = new List<bool>();
        try
        {
            GameWindow.DtrhHapticRunEnded = farewells.Add;
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{type:'ready'}");
            w.HandleMessage("{type:'run-started'}");
            w.HandleMessage("{type:'run-ended',abandoned:true}");
            Assert.Equal(new[] { true }, farewells);                              // an ordinary end keeps the wave

            w.HandleMessage("{type:'run-started'}");
            w.HandleMessage("{type:'run-progress',score:10,durationSec:180}");
            Assert.True(GameWindow.HandleDtrhPanic());                            // end-run posted, watchdog armed
            w.Close();                                                            // what the watchdog does
            Assert.Equal(new[] { true, false }, farewells);                       // the teardown booking: no wave

            w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{type:'ready'}");
            w.HandleMessage("{type:'run-started'}");
            Assert.True(GameWindow.HandleDtrhPanic());
            w.HandleMessage("{type:'run-ended',abandoned:true}");                 // the page settled in time
            Assert.Equal(new[] { true, false, false }, farewells);
            w.Close();
        }
        finally { GameWindow.DtrhHapticRunEnded = old; }
    });
}
