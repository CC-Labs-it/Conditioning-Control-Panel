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

    // panic on now, remote disabled it, lockdown active, lockdown switched panic off -> turn it back on
    [Theory]
    [InlineData(false, false, false, false, true)]   // no Lockdown: panic always comes back
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]    // a Lockdown that left panic alone: comes back
    [InlineData(false, false, true, true, false)]    // a Lockdown holding panic off keeps it off
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, false, false, false, false)]   // already on: nothing to do
    public void Panic_comes_back_on_except_while_a_lockdown_holds_it_off(bool on, bool remoteOff, bool active, bool ldOff, bool turnsOn)
        => Assert.Equal(turnsOn, RemoteHoldings.TurnsPanicBackOn(on, remoteOff, active, ldOff));


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
}
