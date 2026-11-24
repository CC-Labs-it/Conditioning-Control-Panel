using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
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
}
