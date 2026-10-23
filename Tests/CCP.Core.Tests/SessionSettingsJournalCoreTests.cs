using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Main 8a89eaeb0 / 8fe2a80a0 / 36370021a on the Core session: the journal carries exactly what
/// SessionSettingsSnapshot restores, the runner writes it at start and deletes it after the restore,
/// and the Takeover settle runs before the snapshot. The pure journal rules are pinned on the WPF
/// side too (SessionSettingsJournalTests), with WPF's own source scans.
/// </summary>
[Collection(SessionStatics.Name)]
public sealed class SessionSettingsJournalCoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-journal-core-" + Guid.NewGuid().ToString("N"));
    private readonly SessionRunner _runner;

    public SessionSettingsJournalCoreTests()
    {
        Directory.CreateDirectory(_dir);
        PhrasePoolCustody.Seed();
        _runner = new SessionRunner(new SessionLogService()) { JournalDir = _dir };
        CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreSession.NoteUserPhrasePoolEdit = null;
        CoreSession.ReapplyPhrasePoolOverrides = null;
        CoreSession.UserPhrasePoolsWhileOverriding = null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void The_journal_fields_are_exactly_what_the_snapshot_restores()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Session", "SessionSettingsSnapshot.cs"));
        var copied = Regex.Matches(src, @"to\.(\w+) = from\.\1;").Select(m => m.Groups[1].Value).OrderBy(x => x);
        Assert.Equal(copied, SessionSettingsJournal.Fields.OrderBy(x => x));
    }

    [Fact]
    public void The_runner_journals_the_users_values_at_start_and_clears_them_after_the_restore()
    {
        var s = CoreSettings.Current;
        var mine = s.FlashFrequency;
        var settled = false;
        _runner.SettleBeforeSnapshot = () => { settled = true; s.FlashFrequency = mine; };
        _runner.Start(new Session
        {
            Id = "journal", Name = "Journal", DurationMinutes = 1,
            Settings = new SessionSettings { FlashEnabled = true, FlashPerHour = mine + 11 },
        });
        Assert.True(settled);
        var path = Path.Combine(_dir, SessionSettingsJournal.FileName);
        Assert.True(File.Exists(path));
        var journal = JObject.Parse(File.ReadAllText(path));
        Assert.Equal(mine, (int)journal["fields"]![nameof(AppSettings.FlashFrequency)]!);
        Assert.Equal(mine + 11, s.FlashFrequency);

        // A crash here: the next launch puts the user's value back.
        var crashed = new AppSettings { FlashFrequency = mine + 11 };
        SessionSettingsJournal.Apply(File.ReadAllText(path), crashed, null);
        Assert.Equal(mine, crashed.FlashFrequency);

        _runner.Stop();
        Assert.False(File.Exists(path));
        Assert.Equal(mine, s.FlashFrequency);
    }

    [Fact]
    public void An_unseeded_runner_writes_no_journal()
    {
        var bare = new SessionRunner(new SessionLogService());
        Assert.Null(bare.JournalDir);
    }

    [Fact]
    public void The_conditioning_tracker_credits_real_time_not_ticks()
    {
        var s = new AppSettings { TotalConditioningMinutes = 0 };
        var clock = new SteppedClock();
        var t = new ConditioningTimeTracker(clock);
        t.Start(s);
        // 46 ticks 1.3 s apart = 59.8 s; 47 = 61.1 s. Counting ticks credited nothing until tick 60.
        for (var i = 0; i < 46; i++) { clock.Now = clock.Now.AddSeconds(1.3); t.Tick(s); }
        Assert.Equal(0, s.TotalConditioningMinutes);
        clock.Now = clock.Now.AddSeconds(1.3); t.Tick(s);
        Assert.Equal(1, s.TotalConditioningMinutes);
        // A sleep counts at most the cap.
        clock.Now = clock.Now.AddHours(8); t.Tick(s);
        Assert.Equal(1, s.TotalConditioningMinutes);
        t.Stop(s);
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "CCP.Core"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
