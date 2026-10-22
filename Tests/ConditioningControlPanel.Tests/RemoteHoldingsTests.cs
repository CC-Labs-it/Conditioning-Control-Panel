using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remote;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner, 2026-10-22: the wearer's own strict lock / spiral / pink / opacities / Melt are snapshotted
/// when a Remote Control session starts and handed back at every end, crash included; a Lockdown keeps
/// the strict lock it forced. RemoteHoldings is the rule, the source scans pin the wiring.
/// </summary>
public class RemoteHoldingsTests
{
    private static RemoteHoldings TakeFrom(AppSettings s) => RemoteHoldings.Take(f => RemoteHoldings.Read(s, f), 1);

    private static void ControllerWrites(RemoteHoldings h, AppSettings s, RemoteHeld f, int v)
    {
        h.NoteWrite(f, RemoteHoldings.Read(s, f), v);
        RemoteHoldings.Write(s, f, v);
    }

    [Theory]
    [InlineData(RemoteHeld.Spiral, 1, 0)]       // the wearer had the spiral on, the controller stopped it
    [InlineData(RemoteHeld.Spiral, 0, 1)]
    [InlineData(RemoteHeld.Pink, 1, 0)]
    [InlineData(RemoteHeld.Pink, 0, 1)]
    [InlineData(RemoteHeld.SpiralOpacity, 40, 90)]
    [InlineData(RemoteHeld.PinkOpacity, 12, 50)]
    [InlineData(RemoteHeld.BrainDrain, 0, 1)]
    [InlineData(RemoteHeld.BrainDrain, 1, 0)]
    [InlineData(RemoteHeld.StrictLock, 1, 0)]   // the wearer's own strict lock comes back too
    [InlineData(RemoteHeld.StrictLock, 0, 1)]
    public void The_end_gives_the_wearers_own_value_back(RemoteHeld field, int own, int controller)
    {
        var s = new AppSettings();
        RemoteHoldings.Write(s, field, own);
        var h = TakeFrom(s);
        ControllerWrites(h, s, field, controller);
        Assert.Equal(own, h.HandBack(field, RemoteHoldings.Read(s, field)));
        Assert.False(h.Holds(field));
        Assert.Null(h.HandBack(field, controller));   // once
    }

    [Fact]
    public void What_the_wearer_changed_stays_theirs_and_unwritten_fields_are_left_alone()
    {
        var s = new AppSettings { SpiralEnabled = false, PinkFilterEnabled = true, SpiralOpacity = 30 };
        var h = TakeFrom(s);
        ControllerWrites(h, s, RemoteHeld.Spiral, 1);
        s.SpiralEnabled = false;                                 // the wearer switched it off again
        s.PinkFilterEnabled = false;                             // never the controller's
        ControllerWrites(h, s, RemoteHeld.SpiralOpacity, 80);
        s.SpiralOpacity = 55;                                    // the wearer moved the slider
        ControllerWrites(h, s, RemoteHeld.SpiralOpacity, 20);    // Easy / a new ask
        Assert.Equal(1, h.HandBackAllButStrict(s));
        Assert.False(s.SpiralEnabled);
        Assert.False(s.PinkFilterEnabled);
        Assert.Equal(55, s.SpiralOpacity);
    }

    // own, controller wrote, live now, lockdown active, lockdown forced, lockdown recorded -> live, recorded
    [Theory]
    [InlineData(false, true, true, false, false, false, false, null)]   // no Lockdown: the controller's lock goes
    [InlineData(true, false, false, false, false, false, true, null)]   // no Lockdown: the wearer's own lock comes back
    [InlineData(false, true, true, true, true, true, null, false)]      // set BEFORE a forced Lockdown: live stays, record fixed
    [InlineData(false, true, true, true, false, true, false, false)]    // set before an unforced Lockdown: both go back
    [InlineData(false, true, true, true, true, false, null, null)]      // set during a forced Lockdown: nothing to fix
    [InlineData(true, true, true, true, true, true, null, null)]        // the wearer's own lock all along
    public void Strict_lock_hand_back_respects_a_running_lockdown(bool own, bool controller, bool live,
        bool active, bool forced, bool recorded, bool? wantLive, bool? wantRecorded)
    {
        var h = RemoteHoldings.Take(f => f == RemoteHeld.StrictLock ? (own ? 1 : 0) : 0, 1);
        h.NoteWrite(RemoteHeld.StrictLock, own ? 1 : 0, controller ? 1 : 0);
        var back = h.HandBackStrict(live, active, forced, recorded);
        Assert.Equal(wantLive, back.Live);
        Assert.Equal(wantRecorded, back.LockdownRecorded);
        Assert.Equal(new StrictHandBack(null, null), RemoteHoldings.Take(_ => 0, 1).HandBackStrict(true, active, forced, recorded));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Disable_strict_lock_is_refused_only_over_a_lockdowns_forced_lock(bool active, bool forced, bool refused)
        => Assert.Equal(refused, RemoteHoldings.RefusesStrictOff(active, forced));

    [Theory]
    [InlineData(true, false, false, 1)]
    [InlineData(true, true, false, 0)]   // live is Lockdown's forced lock; the wearer's own was off
    [InlineData(false, true, true, 1)]
    public void A_strict_write_under_lockdown_replaces_lockdowns_record(bool current, bool active, bool recorded, int before)
        => Assert.Equal(before, RemoteHoldings.StrictBefore(current, active, recorded));

    [Fact]
    public void A_crash_mid_session_heals_at_the_next_launch_and_the_file_goes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-remote-holdings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var s = new AppSettings { SpiralEnabled = true, PinkFilterEnabled = false, SpiralOpacity = 35, StrictLockEnabled = false };
            var h = TakeFrom(s);
            ControllerWrites(h, s, RemoteHeld.Spiral, 0);
            ControllerWrites(h, s, RemoteHeld.Pink, 1);
            ControllerWrites(h, s, RemoteHeld.SpiralOpacity, 90);
            ControllerWrites(h, s, RemoteHeld.StrictLock, 1);
            RemoteHoldings.Save(dir, h);

            var onDisk = new AppSettings { SpiralEnabled = false, PinkFilterEnabled = true, SpiralOpacity = 90, StrictLockEnabled = true };
            Assert.Equal(4, RemoteHoldings.HealOnLaunch(dir, onDisk));
            Assert.True(onDisk.SpiralEnabled);
            Assert.False(onDisk.PinkFilterEnabled);
            Assert.Equal(35, onDisk.SpiralOpacity);
            Assert.False(onDisk.StrictLockEnabled);
            Assert.False(File.Exists(Path.Combine(dir, RemoteHoldings.FileName)));

            File.WriteAllText(Path.Combine(dir, RemoteHoldings.FileName), "{not json");
            Assert.Equal(0, RemoteHoldings.HealOnLaunch(dir, onDisk));
            Assert.False(File.Exists(Path.Combine(dir, RemoteHoldings.FileName)));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- wiring ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("RemoteControlService.cs", "private void StopAllRemoteEffects(RemoteEndCause cause)")]   // session end, leash cut, panic
    [InlineData("RemoteControlService.cs", "private void StopRemoteTriggeredEffects()")]                 // controller left (stop effects on)
    [InlineData("RemoteControlService.cs", "private void ReleaseRemoteHoldings()")]                      // over the wearer's own run
    [InlineData("Remote/RemoteControlService.V2.cs", "public void ReleaseHoldingsForExit()")]            // app exit
    public void Every_end_path_hands_back_and_never_writes_the_overlays_or_strict_off(string file, string signature)
    {
        var body = CodeOnly(Body(Source(file), signature));
        Assert.Contains("HandBackWearerVisuals();", body);
        foreach (var off in new[] { "SpiralEnabled = false", "PinkFilterEnabled = false", "StrictLockEnabled = false",
                     "EnableSpiral(false)", "EnablePinkFilter(false)" })
            Assert.DoesNotContain(off, body);
    }

    [Fact]
    public void Snapshot_hand_back_and_drop_run_in_order()
    {
        var src = Source("RemoteControlService.cs");
        foreach (var sig in new[] { "private void StopAllRemoteEffects(RemoteEndCause cause)", "private void ReleaseRemoteHoldings()",
                     "private void HandleControllerDisconnectCleanup()" })
            Assert.Contains("ReleaseRemoteSafetyState();", Body(src, sig));
        Assert.Contains("HandBackStrictLock()", Body(src, "private void ReleaseRemoteSafetyState()"));
        Assert.DoesNotContain("_remoteSetStrictLock", src);

        // The session stop restores its own snapshot first; the hand-back comes after it.
        var all = Body(src, "private void StopAllRemoteEffects(RemoteEndCause cause)");
        Assert.True(all.IndexOf("StopSessionFromRemote();", StringComparison.Ordinal)
                    < all.IndexOf("HandBackWearerVisuals();", StringComparison.Ordinal));

        var start = Body(src, "public async Task<string?> StartSessionAsync(string tier)");
        Assert.True(start.IndexOf("IsActive = true;", StringComparison.Ordinal) < start.IndexOf("TakeWearerSnapshot();", StringComparison.Ordinal));
        var cleanup = Body(src, "private void CleanupSession(RemoteEndCause cause = RemoteEndCause.SessionEnded)");
        Assert.True(cleanup.IndexOf("StopAllRemoteEffects(cause)", StringComparison.Ordinal)
                    < cleanup.IndexOf("ResetV2SessionState();", StringComparison.Ordinal));

        var v2 = Source("Remote/RemoteControlService.V2.cs");
        Assert.Contains("DropWearerSnapshot();", Body(v2, "private void ResetV2SessionState()"));
        var exit = Body(v2, "public void ReleaseHoldingsForExit()");
        Assert.True(exit.IndexOf("ReleaseRemoteSafetyState();", StringComparison.Ordinal) < exit.IndexOf("DropWearerSnapshot();", StringComparison.Ordinal));
        Assert.DoesNotContain("SubjectOriginal", v2);   // Easy's own restore is the snapshot's job now
        var easy = Body(v2, "private void ApplyEasy()");
        Assert.Contains("NoteRemoteWrite(RemoteHeld.SpiralOpacity, sv);", easy);
        Assert.Contains("NoteRemoteWrite(RemoteHeld.PinkOpacity, pv);", easy);
        Assert.Contains("NoteRemoteWrite(RemoteHeld.BrainDrain,", Body(v2, "private void SetRemoteBrainDrain(bool on)"));
    }

    [Theory]
    [InlineData("case \"show_pink_filter\":", "RemoteHeld.Pink", "PinkFilterEnabled = true")]
    [InlineData("case \"stop_pink_filter\":", "RemoteHeld.Pink", "PinkFilterEnabled = false")]
    [InlineData("case \"show_spiral\":", "RemoteHeld.Spiral", "SpiralEnabled = true")]
    [InlineData("case \"stop_spiral\":", "RemoteHeld.Spiral", "SpiralEnabled = false")]
    [InlineData("case \"set_pink_opacity\":", "RemoteHeld.PinkOpacity", "PinkFilterOpacity = ")]
    [InlineData("case \"set_spiral_opacity\":", "RemoteHeld.SpiralOpacity", "SpiralOpacity = ")]
    [InlineData("case \"enable_strict_lock\":", "RemoteHeld.StrictLock", "StrictLockEnabled = true")]
    [InlineData("case \"disable_strict_lock\":", "RemoteHeld.StrictLock", "StrictLockEnabled = false")]
    [InlineData("case \"start_session\":", "RemoteHeld.StrictLock", "StrictLockEnabled = true")]
    public void Every_controller_write_is_noted_before_it_lands(string arm, string field, string write)
    {
        var src = Source("RemoteControlService.cs");
        int at = src.IndexOf(arm, StringComparison.Ordinal);
        int note = src.IndexOf("NoteRemoteWrite(" + field, at, StringComparison.Ordinal);
        int lands = src.IndexOf("App.Settings.Current." + write, at, StringComparison.Ordinal);
        Assert.True(at > 0 && note > at && lands > note, arm);
        Assert.True(src.IndexOf("break;", note, StringComparison.Ordinal) > lands, arm + ": noted in the same arm");
        if (arm.Contains("disable_strict_lock"))
            Assert.InRange(src.IndexOf("RemoteHoldings.RefusesStrictOff(", at, StringComparison.Ordinal), at + 1, note);
    }

    [Fact]
    public void Lockdown_takes_the_correction_and_the_crash_heal_runs_after_its_recovery()
    {
        var root = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var ld = File.ReadAllText(Path.Combine(root, "Services", "Haptics", "LockdownService.cs"));
        Assert.Contains("ForcedStrictLock = settings.LockdownForceStrictLock;", Body(ld, "public void Activate(TimeSpan duration)"));
        var correct = Body(ld, "public void CorrectRecordedStrictLock(bool wearersOwn)");
        Assert.Contains("if (!_isActive", correct);
        Assert.Contains("WriteRecoveryFile(", correct);

        var app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        int recover = app.IndexOf("LockdownService.RecoverIfNeeded();", StringComparison.Ordinal);
        Assert.True(recover > 0 && app.IndexOf("RemoteHoldings.HealOnLaunch(UserDataPath", StringComparison.Ordinal) > recover);
    }

    private static string Source(string relUnderServices)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel", "Services" }
            .Concat(relUnderServices.Split('/')).ToArray()));

    private static string CodeOnly(string body)
        => string.Join("\n", body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Body(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int depth = 0;
        for (int i = source.IndexOf('{', at); i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(at, i - at + 1);
        }
        throw new Xunit.Sdk.XunitException("unclosed method: " + signature);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
