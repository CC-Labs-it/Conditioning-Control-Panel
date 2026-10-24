using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Progression;
using Xunit;

namespace CCP.Core.Tests;

public class SessionSettingsJournalTests
{
    [Fact]
    public void JournalFieldsMatchTheCleanStopSnapshot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CCP.Core", "Services", "Session", "SessionSettingsSnapshot.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "CCP.Core", "Services", "Session", "SessionSettingsSnapshot.cs"));
        var copied = Regex.Matches(source, @"to\.(\w+) = from\.\1;")
            .Select(m => m.Groups[1].Value).OrderBy(n => n);
        Assert.Equal(copied, CoreSessionSettingsJournal.Fields.OrderBy(n => n));
    }

    [Fact]
    public void A_journal_round_trips_the_users_values()
    {
        var mine = new AppSettings { AudioDuckingEnabled = false, CorruptionMode = false, FlashOpacity = 30, PinkFilterOpacity = 10, SubAudioVolume = 20, SubliminalDuration = 3 };
        var pool = new Dictionary<string, bool> { ["mine"] = true };
        var json = CoreSessionSettingsJournal.Build(mine, "mod-a", pool, null, null);

        var crashed = new AppSettings { AudioDuckingEnabled = true, CorruptionMode = true, FlashOpacity = 90, PinkFilterOpacity = 60, SubAudioVolume = 80, SubliminalDuration = 9 };
        crashed.SubliminalPool.Clear();
        crashed.SubliminalPool["session"] = true;

        Assert.Equal(CoreSessionSettingsJournal.Fields.Count, CoreSessionSettingsJournal.Apply(json, crashed, "mod-a"));
        Assert.False(crashed.AudioDuckingEnabled);
        Assert.False(crashed.CorruptionMode);
        Assert.Equal(30, crashed.FlashOpacity);
        Assert.Equal(10, crashed.PinkFilterOpacity);
        Assert.Equal(20, crashed.SubAudioVolume);
        Assert.Equal(3, crashed.SubliminalDuration);
        Assert.Equal(new[] { "mine" }, crashed.SubliminalPool.Keys);
    }

    [Fact]
    public void The_pools_come_back_only_on_the_same_mod()
    {
        var json = CoreSessionSettingsJournal.Build(new AppSettings(), "mod-a", new Dictionary<string, bool> { ["mine"] = true }, null, null);
        var other = new AppSettings();
        other.SubliminalPool.Clear();
        other.SubliminalPool["theirs"] = true;
        CoreSessionSettingsJournal.Apply(json, other, "mod-b");
        Assert.Equal(new[] { "theirs" }, other.SubliminalPool.Keys);
    }

    [Fact]
    public void HealOnLaunch_applies_and_deletes_the_journal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            CoreSessionSettingsJournal.Write(dir, CoreSessionSettingsJournal.Build(new AppSettings { AudioDuckingEnabled = false }, null, null, null, null));
            var s = new AppSettings { AudioDuckingEnabled = true };
            Assert.True(CoreSessionSettingsJournal.HealOnLaunch(dir, s));
            Assert.False(s.AudioDuckingEnabled);
            Assert.False(File.Exists(Path.Combine(dir, CoreSessionSettingsJournal.FileName)));
            Assert.False(CoreSessionSettingsJournal.HealOnLaunch(dir, s));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- conditioned minutes ----

    [Fact]
    public void Late_ticks_still_count_their_real_time()
    {
        double pending = 0;
        var minutes = 0;
        // 46 ticks 1.3 s apart = 59.8 s; the old tick counter credited nothing until tick 60 (78 s).
        for (var i = 0; i < 47; i++) minutes += CoreConditioningClock.Tick(ref pending, 1.3);
        Assert.Equal(1, minutes);
        Assert.InRange(pending, 0, 1.3);
    }

    [Fact]
    public void A_long_gap_counts_at_most_the_cap()
    {
        double pending = 0;
        Assert.Equal(0, CoreConditioningClock.Tick(ref pending, 8 * 3600));
        Assert.Equal(CoreConditioningClock.MaxGapSeconds, pending);
        Assert.Equal(0, CoreConditioningClock.Tick(ref pending, -5));
        Assert.Equal(0, CoreConditioningClock.Tick(ref pending, double.NaN));
        Assert.Equal(CoreConditioningClock.MaxGapSeconds, pending);
    }

}
