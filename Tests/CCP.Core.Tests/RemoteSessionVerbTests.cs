using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Remote mind wipe and session verbs (WPF RemoteControlService.cs:1494-1593, MainWindow.RemoteControl.cs:1287-1420).</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteSessionVerbTests
{
    [Fact]
    public void Mind_wipe_verbs_drive_the_core_service_and_a_remote_stop_ends_it()
    {
        var s = CoreSettings.Current;
        var saved = (s.MindWipeFrequency, s.MindWipeVolume, s.StrictLockEnabled, s.PanicKeyEnabled);
        var ran = new List<string>();
        try
        {
            (s.MindWipeFrequency, s.MindWipeVolume) = (12, 40);
            CoreMindWipe.TriggerOnceProvider = () => ran.Add("once");
            CoreMindWipe.ClipCountProvider = () => 0;
            CoreMindWipe.StartProvider = (f, v) => ran.Add($"start {f} {v}");
            CoreMindWipe.StopProvider = () => ran.Add("stop");

            Assert.Null(RemoteCommands.Execute("trigger_mind_wipe", null));   // no clips: WPF plays nothing, still ok
            CoreMindWipe.ClipCountProvider = () => 3;
            Assert.Null(RemoteCommands.Execute("trigger_mind_wipe", null));
            Assert.Null(RemoteCommands.Execute("start_mind_wipe", null));
            Assert.Null(RemoteCommands.Execute("stop_mind_wipe", null));
            Assert.Equal(new[] { "once", "start 12 0.4", "stop" }, ran);

            ran.Clear();
            RemoteCommands.StopEffects(force: false);   // controller left with "stop effects on disconnect"
            Assert.Contains("stop", ran);
        }
        finally
        {
            CoreMindWipe.TriggerOnceProvider = null; CoreMindWipe.ClipCountProvider = null;
            CoreMindWipe.StartProvider = null; CoreMindWipe.StopProvider = null;
            (s.MindWipeFrequency, s.MindWipeVolume, s.StrictLockEnabled, s.PanicKeyEnabled) = saved;
        }
    }

    [Fact]
    public void Start_session_runs_the_named_session_or_the_generic_one_and_honours_strict_lock_unless_leashed()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.FlashFrequency, s.SubliminalFrequency);
        var leashed = LeashGuard.IsLeashed;
        var started = new List<(string Action, Session? Session)>();
        var named = new Session { Id = "s-1", Name = "Named" };
        try
        {
            RemoteCommands.SessionVerb = null;
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("start_session", null));
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("pause_session", null));

            RemoteCommands.SessionVerb = (a, ses) => { started.Add((a, ses)); return null; };
            RemoteCommands.FindSession = id => id == "s-1" ? named : null;
            (s.StrictLockEnabled, s.FlashFrequency, s.SubliminalFrequency) = (false, 17, 4);

            Assert.Null(RemoteCommands.Execute("start_session", JObject.Parse("{\"session_id\":\"s-1\"}")));
            Assert.Same(named, started[^1].Session);
            Assert.False(s.StrictLockEnabled);

            Assert.Null(RemoteCommands.Execute("start_session", JObject.Parse("{\"session_id\":\"gone\",\"strict_lock\":true}")));
            var generic = started[^1].Session!;
            Assert.Equal(("remote_session", 30, 200), (generic.Id, generic.DurationMinutes, generic.BonusXP));
            Assert.Equal((17, 4), (generic.Settings.FlashPerHour, generic.Settings.SubliminalPerMin));
            Assert.True(s.StrictLockEnabled);

            s.StrictLockEnabled = false;
            LeashGuard.IsLeashed = () => true;   // a leashed account keeps its way out: StripStrict
            Assert.Null(RemoteCommands.Execute("start_session", JObject.Parse("{\"strict_lock\":true}")));
            Assert.False(s.StrictLockEnabled);
            LeashGuard.IsLeashed = leashed;

            RemoteCommands.SessionVerb = (_, _) => "panic";   // the head refused: no strict lock either
            Assert.Equal("panic", RemoteCommands.Execute("start_session", JObject.Parse("{\"strict_lock\":true}")));
            Assert.False(s.StrictLockEnabled);
            Assert.Equal("panic", RemoteCommands.Execute("resume_session", null));
        }
        finally
        {
            RemoteCommands.SessionVerb = null; RemoteCommands.FindSession = null;
            LeashGuard.IsLeashed = leashed;
            (s.StrictLockEnabled, s.FlashFrequency, s.SubliminalFrequency) = saved;
        }
    }

    [Fact]
    public void Only_the_pause_a_local_panic_left_holds_the_session_against_a_remote_resume()
    {
        var runner = new SessionRunner(new SessionLogService());
        var session = new Session { Id = "remote-hold", Name = "Hold", DurationMinutes = 5 };
        try
        {
            runner.Start(session);
            RemoteCommands.NoteLocalPanicPause(runner);    // running, not paused: no mark
            runner.Pause();
            Assert.False(RemoteCommands.LocalPanicHoldsSession(runner));

            RemoteCommands.NoteLocalPanicPause(runner);    // the panic left this pause
            Assert.True(RemoteCommands.LocalPanicHoldsSession(runner));

            runner.Resume(); runner.Pause();               // the user resumed; a later pause is a normal one
            Assert.False(RemoteCommands.LocalPanicHoldsSession(runner));

            runner.Stop(); runner.Start(session); runner.Pause();
            RemoteCommands.NoteLocalPanicPause(runner);
            Assert.True(RemoteCommands.LocalPanicHoldsSession(runner));
            runner.Stop(); runner.Start(session); runner.Pause();   // same object, next run, same pause number
            Assert.False(RemoteCommands.LocalPanicHoldsSession(runner));
        }
        finally
        {
            RemoteCommands.NoteLocalPanicPause(null);
            runner.Stop();
            CoreEngine.Stop();
        }
    }
}
