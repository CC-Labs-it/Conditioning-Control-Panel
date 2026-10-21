using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Progression;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A session's values never outlive the session (ccp-bugs #1488, perly 2026-10-21): the journal
/// carries exactly what RestoreSettings hands back, and a launch after a crash puts it back.
/// Plus the conditioned-minute clock (beebee's missed Leash minutes).
/// </summary>
public class SessionSettingsJournalTests
{
    [Fact]
    public void The_journal_carries_exactly_what_a_clean_stop_restores()
    {
        var engine = Read("Services", "Session", "SessionEngine.cs");
        var restore = Member(engine, "private void RestoreSettings()");
        var restored = Regex.Matches(restore, @"current\.(\w+) = _savedSettings\.\1;").Select(m => m.Groups[1].Value).ToHashSet();
        var save = Member(engine, "private void SaveCurrentSettings()");
        var saved = Regex.Matches(save, @"_savedSettings\.(\w+) = current\.\1;").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.Equal(restored.OrderBy(x => x), saved.OrderBy(x => x));
        Assert.Equal(restored.OrderBy(x => x), SessionSettingsJournal.Fields.OrderBy(x => x));
    }

    [Fact]
    public void Every_value_a_session_writes_is_restored()
    {
        var engine = Read("Services", "Session", "SessionEngine.cs");
        var apply = Member(engine, "private void ApplySessionSettings(");
        var written = Regex.Matches(apply, @"current\.(\w+) = ").Select(m => m.Groups[1].Value).ToHashSet();
        written.ExceptWith(new[] { "SubliminalPool", "BouncingTextPool", "LockCardPhrases" });
        Assert.Empty(written.Except(SessionSettingsJournal.Fields));
    }

    [Fact]
    public void A_journal_round_trips_the_users_values()
    {
        var mine = new AppSettings { AudioDuckingEnabled = false, CorruptionMode = false, FlashOpacity = 30, PinkFilterOpacity = 10, SubAudioVolume = 20, SubliminalDuration = 3 };
        var pool = new Dictionary<string, bool> { ["mine"] = true };
        var json = SessionSettingsJournal.Build(mine, "mod-a", pool, null, null);

        var crashed = new AppSettings { AudioDuckingEnabled = true, CorruptionMode = true, FlashOpacity = 90, PinkFilterOpacity = 60, SubAudioVolume = 80, SubliminalDuration = 9 };
        crashed.SubliminalPool.Clear();
        crashed.SubliminalPool["session"] = true;

        Assert.Equal(SessionSettingsJournal.Fields.Count, SessionSettingsJournal.Apply(json, crashed, "mod-a"));
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
        var json = SessionSettingsJournal.Build(new AppSettings(), "mod-a", new Dictionary<string, bool> { ["mine"] = true }, null, null);
        var other = new AppSettings();
        other.SubliminalPool.Clear();
        other.SubliminalPool["theirs"] = true;
        SessionSettingsJournal.Apply(json, other, "mod-b");
        Assert.Equal(new[] { "theirs" }, other.SubliminalPool.Keys);
    }

    [Fact]
    public void The_engine_writes_the_journal_at_start_and_clears_it_after_a_restore_and_the_app_heals_at_launch()
    {
        var engine = Read("Services", "Session", "SessionEngine.cs");
        Assert.Contains("SessionSettingsJournal.Write(App.UserDataPath", Member(engine, "private void SaveCurrentSettings()"));
        var restore = Member(engine, "private void RestoreSettings()");
        Assert.True(restore.IndexOf("SessionSettingsJournal.Clear(App.UserDataPath);", StringComparison.Ordinal)
            > restore.IndexOf("_savedSettings = null;", StringComparison.Ordinal));

        var app = Read("App.xaml.cs");
        var heal = app.IndexOf("SessionSettingsJournal.HealOnLaunch(UserDataPath, Settings.Current)", StringComparison.Ordinal);
        Assert.True(heal > 0 && heal < app.IndexOf("Mods = new ModService();", StringComparison.Ordinal));
    }

    [Fact]
    public void HealOnLaunch_applies_and_deletes_the_journal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SessionSettingsJournal.Write(dir, SessionSettingsJournal.Build(new AppSettings { AudioDuckingEnabled = false }, null, null, null, null));
            var s = new AppSettings { AudioDuckingEnabled = true };
            Assert.True(SessionSettingsJournal.HealOnLaunch(dir, s));
            Assert.False(s.AudioDuckingEnabled);
            Assert.False(File.Exists(Path.Combine(dir, SessionSettingsJournal.FileName)));
            Assert.False(SessionSettingsJournal.HealOnLaunch(dir, s));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_takeover_pulse_is_settled_before_the_session_snapshot()
    {
        var engine = Read("Services", "Session", "SessionEngine.cs");
        var settle = engine.IndexOf("App.Autonomy?.SettleSettingPulses();", StringComparison.Ordinal);
        var snapshot = engine.IndexOf("            SaveCurrentSettings();", StringComparison.Ordinal);
        Assert.True(settle > 0 && snapshot > settle && snapshot - settle < 300);

        var settleBody = Member(Read("Services", "AutonomyService.cs"), "public void SettleSettingPulses()");
        Assert.Contains("_pinkFilterPulseActive = false;", settleBody);
        Assert.Contains("_spiralPulseActive = false;", settleBody);
        // Narrow on purpose: no wallpaper, bubbles or overlay-service stop at a session start.
        Assert.DoesNotContain("Wallpaper", settleBody);
        Assert.DoesNotContain("Overlay?.Stop", settleBody);
        Assert.DoesNotContain("_globalPulseGeneration", settleBody);
    }

    // ---- conditioned minutes ----

    [Fact]
    public void Late_ticks_still_count_their_real_time()
    {
        double pending = 0;
        var minutes = 0;
        // 46 ticks 1.3 s apart = 59.8 s; the old tick counter credited nothing until tick 60 (78 s).
        for (var i = 0; i < 47; i++) minutes += ConditioningClock.Tick(ref pending, 1.3);
        Assert.Equal(1, minutes);
        Assert.InRange(pending, 0, 1.3);
    }

    [Fact]
    public void A_long_gap_counts_at_most_the_cap()
    {
        double pending = 0;
        Assert.Equal(0, ConditioningClock.Tick(ref pending, 8 * 3600));
        Assert.Equal(ConditioningClock.MaxGapSeconds, pending);
        Assert.Equal(0, ConditioningClock.Tick(ref pending, -5));
        Assert.Equal(0, ConditioningClock.Tick(ref pending, double.NaN));
        Assert.Equal(ConditioningClock.MaxGapSeconds, pending);
    }

    [Fact]
    public void The_engine_timer_credits_through_the_clock()
    {
        var ui = Read("MainWindow", "MainWindow.UiUpdates.cs");
        Assert.Contains("Services.Progression.ConditioningClock.Tick(ref _conditioningPendingSeconds,", ui);
        Assert.DoesNotContain("if (_conditioningTimeSecondCounter >= 60)", ui);
    }

    private static string Member(string src, string signature)
    {
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        var open = src.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(at, i - at + 1);
        }
        return src.Substring(at);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
