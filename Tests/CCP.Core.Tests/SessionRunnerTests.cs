using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Tests that share CoreSettings.Current, CoreEngine and the CoreSession seams run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class SessionStatics { public const string Name = "SessionStatics"; }

/// <summary>SessionRunner headless, driven by Tick, against WPF SessionEngine's run/stop/log behaviour.</summary>
[Collection(SessionStatics.Name)]
public sealed class SessionRunnerTests : IDisposable
{
    private const string Id = "runner-golden";
    private static readonly string[] AllowedSettingsChanges =
        { "TotalSessions", "RecentSessionStartsUtc", "LastSessionModId", "SameModRun",
          // Restack b213: the season session and feature counts are kept, as WPF (SessionEngine.cs:296-318).
          "SeasonStatsSeason", "SeasonSessionsStarted", "SeasonFeatureUse" };
    private static readonly string[] Timestamps = { "started_at", "ended_at", "timestamp", "session_time_seconds" };

    private readonly SessionLogService _logs = new();
    private readonly SessionRunner _runner;
    private SessionLog? _ready;

    public SessionRunnerTests()
    {
        Assert.StartsWith(Path.GetTempPath(), _logs.LogsFolder);   // RoadmapTestProfile sandbox
        DeleteLogs();
        PhrasePoolCustody.Seed();   // the head's job (Program.cs), not the runner's
        _runner = new SessionRunner(_logs);
        _logs.LogReady += (_, e) => _ready = e.Log;
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
        DeleteLogs();
    }

    private void DeleteLogs()
    {
        foreach (var f in Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json")) File.Delete(f);
    }

    private static Session OneMinute() => new()
    {
        Id = Id, Name = "Runner Golden", Icon = "🌸", Difficulty = SessionDifficulty.Medium, DurationMinutes = 1,
        Settings = new SessionSettings
        {
            FlashEnabled = true, FlashPerHour = 77,
            SubliminalEnabled = true, SubliminalFrames = 9, SubliminalPhrases = { "zzsession" },
            BouncingTextEnabled = true, BouncingTextPhrases = { "zzbounce" },
            LockCardEnabled = true, LockCardFrequency = 7, LockCardPhrases = { "zzlock" },
        },
    };

    // WPF SessionEngine.cs:1498-1525, :818-832, :925-935, :521/:570.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void SessionBubblesApplyDelayRampPauseAndRestore(bool enabled, int delay)
    {
        var s = CoreSettings.Current;
        var saved = (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable);
        var hooks = (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction);
        bool running = false;
        int refreshes = 0;
        CoreBubbles.StartAction = () => running = true;
        CoreBubbles.StopAction = () => running = false;
        CoreBubbles.RefreshFrequencyAction = () => refreshes++;
        try
        {
            (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable) = (true, 37, true);
            var session = OneMinute();
            session.DurationMinutes = 20;
            var ss = session.Settings;
            (ss.BubblesEnabled, ss.BubblesStartMinute, ss.BubblesFrequency, ss.BubblesClickable) = (enabled, delay, 5, false);
            _runner.Start(session);
            Assert.Equal(enabled && delay == 0, running);
            Assert.Equal(enabled && delay == 0, s.BubblesEnabled);
            if (enabled) Assert.False(s.BubblesClickable);
            _runner.Tick(TimeSpan.FromMinutes(1));
            Assert.Equal(enabled && delay == 0, running);
            _runner.Tick(TimeSpan.FromMinutes(2));
            Assert.Equal(enabled, running);
            if (enabled)
            {
                Assert.Equal(5, s.BubblesFrequency);
                _runner.Pause();
                Assert.False(running);
                _runner.Resume();
                Assert.True(running);
                var before = refreshes;
                _runner.Tick(TimeSpan.FromMinutes(7));
                Assert.Equal(delay > 0 ? 6 : 5, s.BubblesFrequency);
                Assert.Equal(before + (delay > 0 ? 1 : 0), refreshes);
            }
            _runner.Stop();
            Assert.False(running);
            Assert.Equal((true, 37, true), (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable));
        }
        finally
        {
            _runner.Stop();
            (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction) = hooks;
            (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable) = saved;
        }
    }

    // WPF SessionEngine.cs:1013-1067: active-time scheduling, wall-time burst expiry.
    [Fact]
    public void SessionBubblesBurstsExpireAcrossPauseAndResetOnNextRun()
    {
        var s = CoreSettings.Current;
        var saved = (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable);
        var hooks = (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction);
        bool running = false;
        CoreBubbles.StartAction = () => running = true;
        CoreBubbles.StopAction = () => running = false;
        CoreBubbles.RefreshFrequencyAction = () => { };
        try
        {
            var session = OneMinute();
            session.DurationMinutes = 20;
            var ss = session.Settings;
            (ss.BubblesEnabled, ss.BubblesIntermittent, ss.BubblesBurstCount, ss.BubblesPerBurst) = (true, true, 1, 4);
            _runner.Start(session);
            Assert.False(running);
            var now = new DateTime(2026, 1, 1);
            _runner.Tick(TimeSpan.FromMinutes(1), now);
            Assert.False(running);
            _runner.Tick(TimeSpan.FromMinutes(4), now);
            Assert.True(running);
            Assert.Equal(8, s.BubblesFrequency);
            _runner.Pause();
            Assert.False(running);
            _runner.Tick(TimeSpan.FromMinutes(4), now.AddMinutes(3));
            Assert.False(running);
            _runner.Resume();
            Assert.True(running); // WPF resumes from the session switch before the next tick.
            _runner.Tick(TimeSpan.FromMinutes(4), now.AddMinutes(3));
            Assert.False(running);
            _runner.Tick(TimeSpan.FromMinutes(10), now.AddMinutes(10));
            Assert.False(running); // exactly one burst
            _runner.Stop();
            _runner.Start(session);
            _runner.Tick(TimeSpan.FromMinutes(4), now.AddMinutes(20));
            Assert.True(running); // new run resets the schedule
        }
        finally
        {
            _runner.Stop();
            (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction) = hooks;
            (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable) = saved;
        }
    }

    // WPF SessionEngine.cs:570: Resume uses the session switch even while the live flag is off.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionBubblesResumeBeforeDelayedStartOrBetweenBursts(bool intermittent)
    {
        var s = CoreSettings.Current;
        var saved = (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable);
        var hooks = (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction);
        bool running = false;
        CoreBubbles.StartAction = () => running = true;
        CoreBubbles.StopAction = () => running = false;
        CoreBubbles.RefreshFrequencyAction = () => { };
        try
        {
            var session = OneMinute();
            session.DurationMinutes = 30;
            var ss = session.Settings;
            ss.BubblesEnabled = true;
            ss.BubblesIntermittent = intermittent;
            ss.BubblesStartMinute = 8;
            ss.BubblesBurstCount = 2;
            ss.BubblesGapMin = ss.BubblesGapMax = 10;
            _runner.Start(session);
            if (intermittent)
            {
                var now = new DateTime(2026, 1, 1);
                _runner.Tick(TimeSpan.FromMinutes(4), now);
                Assert.True(running);
                _runner.Tick(TimeSpan.FromMinutes(4), now.AddMinutes(3));
            }
            else _runner.Tick(TimeSpan.FromMinutes(1));
            Assert.False(s.BubblesEnabled);
            Assert.False(running);
            _runner.Pause();
            _runner.Resume();
            Assert.True(running);
            Assert.False(s.BubblesEnabled); // Resume requests Start without rewriting the timeline flag.
        }
        finally
        {
            _runner.Stop();
            (CoreBubbles.StartAction, CoreBubbles.StopAction, CoreBubbles.RefreshFrequencyAction) = hooks;
            (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable) = saved;
        }
    }

    [Fact]
    public void ThrowingProgressDoesNotEscapeOrPreventTheNextTick()
    {
        _runner.Start(OneMinute());
        var calls = 0;
        _runner.Ticked += () => { calls++; throw new InvalidOperationException("progress probe"); };
        _runner.Tick(TimeSpan.FromSeconds(1));
        _runner.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(2, calls);
        Assert.True(_runner.IsRunning);
        _runner.Tick(TimeSpan.FromMinutes(1));
        Assert.False(_runner.IsRunning);
        Assert.NotNull(_ready);
    }

    [Fact]
    public void ThrowingSettingsRestoreStillReleasesPoolsAndRaisesStopped()
    {
        var stopped = false;
        var threw = false;
        var snapshot = SessionSettingsSnapshot.Capture(CoreSettings.Current);
        _runner.Start(OneMinute());
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == "FlashEnabled") { threw = true; throw new InvalidOperationException("restore probe"); }
        };
        var settings = CoreSettings.Current;
        settings.PropertyChanged += handler;
        _runner.Stopped += (_, _) => stopped = true;
        try
        {
            _runner.Stop();
            Assert.True(threw);
            Assert.True(stopped);
            Assert.Null(_runner.CurrentSession);
            Assert.False(CoreEngine.IsRunning);
        }
        finally { settings.PropertyChanged -= handler; snapshot.RestoreTo(settings); }
    }

    [Fact]
    public void UserResumeRestoresDeferredStartsWithoutCountingANewRun()
    {
        var session = OneMinute();
        session.Settings.FlashStartMinute = 1;
        session.Settings.MindWipeEnabled = false;
        _runner.Start(session);
        _runner.Pause();
        CoreEngine.Stop();
        var count = CoreSettings.Current.TotalSessions;
        CoreEngine.Start(resumingSession: true);
        _runner.ResumeAfterEngineStart();
        Assert.False(_runner.IsPaused);
        Assert.False(CoreFlash.IsRunning);
        Assert.Equal(count, CoreSettings.Current.TotalSessions);
    }

    [Fact]
    public void PanicStartRestoresTheRemainingLockCardWindow()
    {
        var session = OneMinute();
        session.DurationMinutes = 2;
        _runner.Start(session);
        _runner.Pause();
        _runner._pausedElapsed = TimeSpan.FromSeconds(30);
        CoreEngine.Stop();
        CoreEngine.Start(resumingSession: true);
        Assert.Null(LockCardScheduler.Instance.LastWindowMinutes);
        _runner.ResumeAfterEngineStart();
        Assert.InRange(LockCardScheduler.Instance.LastWindowMinutes ?? -1, 1.49, 1.5);
    }

    [Fact]
    public void PauseAndResumeRepaintEvenWhileTheTimerIsHeld()
    {
        _runner.Start(OneMinute());
        var paused = new System.Collections.Generic.List<bool>();
        _runner.Ticked += () => paused.Add(_runner.IsPaused);
        _runner.Pause();
        _runner.Resume();
        Assert.Equal(new[] { true, false }, paused);
        Assert.Equal(" [PAUSED]", SessionClockSafety.PauseMark(true, "PAUSED"));
        Assert.False(SessionClockSafety.ResumeStartsEngine(false, true));
    }

    [Fact]
    public void StepFailuresReportOncePerStepAndResetForTheNextSession()
    {
        var failures = new SessionClockSafety.StepFailures();
        Assert.True(failures.Note("tick:ramp"));
        Assert.False(failures.Note("tick:ramp"));
        Assert.True(failures.Note("stop"));
        Assert.Equal("stop x1, tick:ramp x2", failures.Summary());
        failures.Clear();
        Assert.False(failures.Any);
        Assert.True(failures.Note("tick:ramp"));
    }

    [Fact]
    public void RaffleDatesUseUtcEvenWhenTheLocalSheetIsAhead()
    {
        var utc = new DateTime(2026, 10, 8, 23, 30, 0, DateTimeKind.Utc);
        var dates = new System.Collections.Generic.HashSet<DateTime> { utc.Date, utc.Date.AddDays(1) };
        var cells = ConditioningControlPanel.Services.Chaster.LockCalendar.CellsFor(utc.Date, utc.Date.AddDays(1),
            utc.Date.AddDays(1), dates, utc);
        Assert.True(cells[0].RaffleCounted);
        Assert.False(cells[1].RaffleCounted);
        Assert.False(cells[2].RaffleCounted); // key never gets a dot
    }

    private static JObject SettingsJson() =>
        JObject.Parse(JsonConvert.SerializeObject(CoreSettings.Current, Formatting.Indented));   // = settings.json

    [Theory]
    [InlineData("show_pink_filter")]
    [InlineData("show_spiral")]
    public void Local_pause_stops_remote_overlays_and_later_remote_show_can_restart_them(string verb)
    {
        var oldRefresh = RemoteCommands.RefreshOverlays;
        var refreshes = 0;
        RemoteCommands.RefreshOverlays = () => refreshes++;
        try
        {
            _runner.Start(OneMinute());
            Assert.Null(RemoteCommands.Execute(verb, null));
            Assert.True(RemoteCommands.OverlayRunning);
            var beforePause = refreshes;
            _runner.Pause();
            Assert.False(RemoteCommands.OverlayRunning);
            Assert.True(RemoteCommands.OverlaysStopped);
            Assert.Equal(beforePause + 1, refreshes);
            Assert.True(verb == "show_pink_filter" ? CoreSettings.Current.PinkFilterEnabled : CoreSettings.Current.SpiralEnabled);
            _runner.Resume();
            Assert.False(RemoteCommands.OverlaysStopped);
            _runner.Pause();
            Assert.Null(RemoteCommands.Execute(verb, null));
            Assert.True(_runner.IsPaused);
            Assert.True(RemoteCommands.OverlayRunning);
            Assert.False(RemoteCommands.OverlaysStopped);
        }
        finally
        {
            _runner.Stop();
            RemoteCommands.StopOverlays(); RemoteCommands.ReleaseOverlayStop();
            RemoteCommands.RefreshOverlays = oldRefresh;
        }
    }

    [Fact]
    public void Local_resume_releases_a_remote_overlay_stop_after_panic()
    {
        var oldRefresh = RemoteCommands.RefreshOverlays;
        RemoteCommands.RefreshOverlays = () => { };
        try
        {
            _runner.Start(OneMinute());
            Assert.Null(RemoteCommands.Execute("show_spiral", null));
            _runner.Pause();
            CoreEngine.Stop();
            Assert.True(RemoteCommands.OverlaysStopped);
            _runner.Resume();
            Assert.False(RemoteCommands.OverlaysStopped);
            Assert.Null(RemoteCommands.Execute("show_spiral", null));
            RemoteCommands.StopOverlays();
            Assert.True(RemoteCommands.OverlaysStopped);
            CoreEngine.Start();
            Assert.False(RemoteCommands.OverlaysStopped);
        }
        finally
        {
            RemoteCommands.StopOverlays(); RemoteCommands.ReleaseOverlayStop();
            RemoteCommands.RefreshOverlays = oldRefresh;
        }
    }

    [Fact]
    public void OneMinuteSession_CompletesByTick_SavesLog_RestoresSettings_ReleasesPools()
    {
        var s = CoreSettings.Current;
        s.SubliminalDuration = 3;
        var before = SettingsJson();
        var startsBefore = s.SeasonSessionsStarted;
        var flashBefore = s.SeasonFeatureUse.TryGetValue(SeasonFeatureKeys.Flash, out var f) ? f : 0;

        _runner.Start(OneMinute());
        Assert.Equal(startsBefore + 1, s.SeasonSessionsStarted);   // once per start
        Assert.Equal(flashBefore + 1, s.SeasonFeatureUse[SeasonFeatureKeys.Flash]);
        Assert.True(CoreSession.IsSessionRunning && CoreEngine.IsRunning);
        Assert.NotNull(CoreSession.UserPhrasePoolsWhileOverriding!());
        Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && LockCardScheduler.Instance.IsRunning);
        Assert.Equal((77, 9, 7), (s.FlashFrequency, s.SubliminalDuration, s.LockCardFrequency));
        Assert.True(s.SubliminalPool["zzsession"]);

        _logs.RecordImages(new[] { "/pics/a.png" });
        var ticks = 0;
        _runner.Ticked += () => ticks++;   // the head's clock labels (WPF ProgressUpdated)
        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.True(_runner.IsRunning);
        Assert.Equal(1, ticks);
        _runner.Tick(TimeSpan.FromSeconds(60));

        Assert.False(_runner.IsRunning || CoreSession.IsSessionRunning || CoreEngine.IsRunning);
        Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
        Assert.Null(CoreSession.UserPhrasePoolsWhileOverriding!());

        var after = SettingsJson();
        var changed = before.Properties().Where(p => !JToken.DeepEquals(p.Value, after[p.Name])).Select(p => p.Name).ToList();
        Assert.Contains("TotalSessions", changed);
        Assert.Empty(changed.Except(AllowedSettingsChanges));
        Assert.Equal(before.Properties().Select(p => p.Name), after.Properties().Select(p => p.Name));   // no new keys

        Assert.NotNull(_ready);
        var file = Path.Combine(_logs.LogsFolder, $"{_ready!.StartedAt:yyyyMMdd_HHmmss}_{Id}.json");
        var saved = JObject.Parse(File.ReadAllText(file));
        var golden = JObject.Parse(File.ReadAllText(Fixture()));
        foreach (var o in new[] { saved, golden })
            foreach (var t in o.Descendants().OfType<JProperty>().Where(p => Timestamps.Contains(p.Name)).ToList()) t.Remove();
        Assert.Equal(golden.ToString(), saved.ToString());
    }

    [Fact]
    public void EarlyStop_IsNotCompleted()
    {
        _runner.Start(OneMinute());
        _logs.RecordImages(new[] { "/pics/a.png" });
        _runner.Tick(TimeSpan.FromSeconds(30));
        _runner.Stop();

        Assert.False(_runner.IsRunning || CoreEngine.IsRunning);
        Assert.False(_ready!.Completed);
        Assert.Equal(0, _ready.XPEarned);
        Assert.Single(Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json"));
    }

    [Fact]
    public void ShortSessionWithoutMedia_IsNotSaved()
    {
        _runner.Start(OneMinute());
        _runner.Stop();

        Assert.NotNull(_ready);
        Assert.Empty(Directory.GetFiles(_logs.LogsFolder, "*_" + Id + ".json"));
    }

    [Fact]
    public void DeferredStarts_FireAtTheirMinute_LockCardsGetTheRemainingWindow()
    {
        int btStarts = 0;
        CoreBouncingText.StartAction = () => btStarts++;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.BouncingTextStartMinute = 1;
        session.Settings.LockCardStartMinute = 1;
        _runner.Start(session);
        btStarts = 0;   // the engine arms by the saved flag first; the session then stops and defers it
        Assert.False(LockCardScheduler.Instance.IsRunning);

        _runner.Tick(TimeSpan.FromSeconds(59));
        Assert.Equal(0, btStarts);
        _runner.Tick(TimeSpan.FromSeconds(60));
        Assert.Equal(1, btStarts);
        Assert.True(LockCardScheduler.Instance.IsRunning);
        Assert.Equal(1.0, LockCardScheduler.Instance.LastWindowMinutes);   // #736: minutes left, not the duration
    }

    [Fact]
    public void UserRestartReassertsDeferredEffectsAndDoesNotCountAnotherSession()
    {
        var session = OneMinute();
        session.DurationMinutes = 10;
        session.Settings.FlashStartMinute = 3;
        session.Settings.SubliminalStartMinute = 3;
        session.Settings.LockCardStartMinute = 3;
        session.Settings.BouncingTextStartMinute = 3;
        var bouncing = false;
        CoreBouncingText.StartAction = () => bouncing = true;
        CoreBouncingText.StopAction = () => bouncing = false;
        var mindWipe = false;
        var oldMindWipeEnabled = CoreSettings.Current.MindWipeEnabled;
        var oldAudioOnly = CoreSettings.Current.AudioOnlySession;
        CoreMindWipe.StartProvider = (_, _) => mindWipe = true;
        CoreMindWipe.StopProvider = () => mindWipe = false;
        try
        {
            CoreSettings.Current.MindWipeEnabled = true;
            CoreSettings.Current.AudioOnlySession = false;
            _runner.Start(session);
            var count = CoreSettings.Current.TotalSessions;
            _runner.Pause();
            CoreEngine.Stop();
            CoreEngine.Start(resumingSession: true);
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && bouncing && mindWipe);
            _runner.ResumeAfterEngineStart();   // the trunk folds Resume and the reassert into one call
            Assert.Equal(count, CoreSettings.Current.TotalSessions);
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning || bouncing || mindWipe);
            _runner.Tick(TimeSpan.FromMinutes(3));
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && LockCardScheduler.Instance.IsRunning && bouncing);
        }
        finally
        {
            _runner.Stop();
            CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
            CoreMindWipe.StartProvider = null;
            CoreMindWipe.StopProvider = null;
            CoreSettings.Current.MindWipeEnabled = oldMindWipeEnabled;
            CoreSettings.Current.AudioOnlySession = oldAudioOnly;
        }
    }

    [Fact]
    public void RestartRearmsActiveLockCardsWithinTheRemainingSession()
    {
        var session = OneMinute();
        session.DurationMinutes = 10;
        var oldAudioOnly = CoreSettings.Current.AudioOnlySession;
        try
        {
            CoreSettings.Current.AudioOnlySession = false;
            _runner.Start(session);
            _runner.Pause();
            CoreEngine.Stop();
            CoreEngine.Start(resumingSession: true);
            Assert.True(LockCardScheduler.Instance.IsRunning);
            Assert.Null(LockCardScheduler.Instance.LastWindowMinutes);
            _runner.ResumeAfterEngineStart();   // the trunk folds Resume and the reassert into one call
            Assert.True(LockCardScheduler.Instance.IsRunning);
            Assert.NotNull(LockCardScheduler.Instance.LastWindowMinutes);
            Assert.InRange(LockCardScheduler.Instance.LastWindowMinutes.Value, 9.9, 10.0);
        }
        finally
        {
            _runner.Stop();
            CoreSettings.Current.AudioOnlySession = oldAudioOnly;
        }
    }

    [Fact]
    public void Ledger_IsPersistedBeforeTheSessionOverrides()
    {
        var old = CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        CoreSettings.ServiceProvider = () => svc;
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        try
        {
            svc.Current.FlashFrequency = 20;
            var starts = svc.Current.RecentSessionStartsUtc.Count;
            _runner.Start(OneMinute());

            var disk = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(starts + 1, ((JArray)disk["RecentSessionStartsUtc"]!).Count);
            Assert.Equal(20, (int)disk["FlashFrequency"]!);   // the user's, not the session's 77
            Assert.Equal(77, svc.Current.FlashFrequency);
        }
        finally
        {
            _runner.Stop();
            svc.SaveImmediate();
            svc.SealForReset();
            CoreSettings.ServiceProvider = old;
            foreach (var f in Directory.GetFiles(CorePaths.UserData, "settings*")) File.Delete(f);
        }
    }

    [Fact]
    public void Pause_FreezesTheClock_StopsFeatures_AndCostsOneHundredXp()
    {
        var old = CoreProgression.AddXPProvider;
        double banked = 0;
        var oldXp = CoreSettings.Current.PlayerXP;
        // A bank that really banks: XPEarned is the ledger's gain, not the award handed over.
        CoreProgression.AddXPProvider = (xp, _) => { banked += xp; CoreSettings.Current.PlayerXP += xp; };
        try
        {
            var session = OneMinute();
            session.BonusXP = 800;
            _runner.Start(session);
            _logs.RecordImages(new[] { "/pics/a.png" });
            _runner.Tick(TimeSpan.FromSeconds(20));
            _runner.Pause();
            _runner.Pause();   // already paused: no second charge

            Assert.True(_runner.IsPaused && _runner.IsRunning && CoreSession.IsSessionRunning);
            Assert.Equal((1, 100), (_runner.PauseCount, _runner.XPPenalty));
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            var frozen = _runner.Elapsed;
            _runner.Tick(TimeSpan.FromSeconds(90));   // a paused session never completes
            Assert.True(_runner.IsRunning);
            Assert.Equal(frozen, _runner.Elapsed);

            _runner.Resume();
            _runner.Tick(TimeSpan.FromSeconds(60));

            var level = CoreSettings.Current.PlayerLevel;
            var expected = SessionXp.Compute(800, 1, level, TimeSpan.FromSeconds(60));
            Assert.NotEqual(SessionXp.Compute(800, 0, level, TimeSpan.FromSeconds(60)), expected);
            Assert.Equal(expected, _ready!.XPEarned);
            Assert.Equal(expected, banked);
        }
        finally { CoreProgression.AddXPProvider = old; CoreSettings.Current.PlayerXP = oldXp; }
    }

    [Fact]
    public void AGateThatRefusesTheAward_LogsZeroBanked()
    {
        var old = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (_, _) => { };   // e.g. signed out: ProgressionBank's login gate
        try
        {
            var session = OneMinute();
            session.BonusXP = 800;
            _runner.Start(session);
            _runner.Tick(TimeSpan.FromSeconds(61));
            Assert.Equal(0, _ready!.XPEarned);
        }
        finally { CoreProgression.AddXPProvider = old; }
    }

    private sealed class QuizHost : IPopQuizHost
    {
        public int Closes;
        public bool IsQuizOpen => false;
        public bool IsLockCardOpen => false;
        public bool IsInteractionBusy => false;
        public bool Defer(Action replay) => false;
        public void DropDeferred() { }
        public void Open(bool isTest) { }
        public void CloseAll() => Closes++;
    }

    /// <summary>WPF SessionEngine pop quiz: start at session start (:1628), stop + close on pause (:527),
    /// back on resume only while enabled (:574), stop at the end (:372), toggle and rate restored (:1771).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PopQuiz_FollowsTheUserToggle_ThroughPauseResumeAndStop(bool enabled)
    {
        var s = CoreSettings.Current;
        var (oldEnabled, oldFreq, oldQuiz) = (s.PopQuizEnabled, s.PopQuizFrequency, CoreEngine.PopQuiz);
        var host = new QuizHost();
        using var quiz = new PopQuizScheduler(host);
        CoreEngine.PopQuiz = quiz;
        s.PopQuizFrequency = 5;
        try
        {
            // Engine already up with the opposite toggle: the session start itself must start/stop it.
            s.PopQuizEnabled = !enabled;
            CoreEngine.Start();
            s.PopQuizEnabled = enabled;
            _runner.Start(OneMinute());
            Assert.Equal(enabled, quiz.IsRunning);
            host.Closes = 0;

            _runner.Pause();
            Assert.False(quiz.IsRunning);
            Assert.Equal(enabled ? 1 : 0, host.Closes);

            _runner.Resume();
            Assert.Equal(enabled, quiz.IsRunning);

            // The rate is session-scoped; the toggle is the player's own and sticks (7.1.5, WPF f30b54f31).
            s.PopQuizEnabled = !enabled;
            s.PopQuizFrequency = 99;
            _runner.Stop();
            Assert.False(quiz.IsRunning);
            Assert.Equal((!enabled, 5), (s.PopQuizEnabled, s.PopQuizFrequency));
        }
        finally
        {
            _runner.Stop();
            CoreEngine.PopQuiz = oldQuiz;
            (s.PopQuizEnabled, s.PopQuizFrequency) = (oldEnabled, oldFreq);
        }
    }

    [Fact]
    public void MindWipe_StartsInSessionModeAtItsMinute_StopsOnPause_ResumesPlain_StopsAtTheEnd()
    {
        // WPF SessionEngine.cs:207-228 (StartSession, deferred), :528 (pause), :575-576 (plain resume), :368 (stop).
        var old = (CoreMindWipe.StartSessionProvider, CoreMindWipe.StartProvider, CoreMindWipe.StopProvider);
        var calls = new System.Collections.Generic.List<string>();
        CoreMindWipe.StartSessionProvider = (b, v) => calls.Add($"session {b} {v * 100:0}");
        CoreMindWipe.StartProvider = (f, v) => calls.Add($"start {f} {v * 100:0}");
        CoreMindWipe.StopProvider = () => calls.Add("stop");
        try
        {
            var session = OneMinute();
            session.DurationMinutes = 3;
            session.Settings.MindWipeEnabled = true;
            session.Settings.MindWipeStartMinute = 1;
            session.Settings.MindWipeBaseMultiplier = 2;
            session.Settings.MindWipeVolume = 60;
            _runner.Start(session);
            Assert.DoesNotContain(calls, c => c.StartsWith("session"));   // deferred to minute 1
            _runner.Pause();
            _runner.Resume();
            Assert.DoesNotContain(calls, c => c.StartsWith("session") || c.StartsWith("start"));
            _runner.Tick(TimeSpan.FromSeconds(61));
            Assert.Equal("session 2 60", calls.Last());

            calls.Clear();
            _runner.Pause();
            Assert.Equal(new[] { "stop" }, calls);
            _runner.Resume();
            Assert.Equal(new[] { "stop", "start 2 60" }, calls);

            calls.Clear();
            _runner.Stop();
            Assert.Equal("stop", calls.First());

            calls.Clear();
            session.Settings.MindWipeStartMinute = 0;
            _runner.Start(session);
            Assert.Equal("session 2 60", calls.Last());
        }
        finally { (CoreMindWipe.StartSessionProvider, CoreMindWipe.StartProvider, CoreMindWipe.StopProvider) = old; }
    }

    [Fact]
    public void BrainDrain_SessionOwnsDelayedAudioRampPauseEndAndRestore()
    {
        // WPF SessionEngine.cs:843-856,969-990,1484-1495,529,579,1698-1702.
        var old = (CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain, CoreEngine.SetBrainDrainIntensity);
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, s.BrainDrainIntensity);
        var calls = new System.Collections.Generic.List<string>();
        var ramps = new System.Collections.Generic.List<double>();
        CoreEngine.StartBrainDrain = () => calls.Add($"start {s.BrainDrainEnabled} {s.BrainDrainIntensity}");
        CoreEngine.StopBrainDrain = () => calls.Add("stop");
        CoreEngine.SetBrainDrainIntensity = ramps.Add;
        try
        {
            s.BrainDrainEnabled = true; s.BrainDrainIntensity = 73;
            var session = OneMinute(); session.DurationMinutes = 4;
            var ss = session.Settings;
            ss.BrainDrainEnabled = true; ss.BrainDrainStartMinute = 1; ss.BrainDrainEndMinute = 3;
            ss.BrainDrainStartIntensity = 20; ss.BrainDrainEndIntensity = 80; ss.RampCurve = RampCurve.EaseIn;
            _runner.Start(session);
            Assert.Equal(new[] { "start True 73", "stop" }, calls);
            Assert.False(s.BrainDrainEnabled);
            calls.Clear(); _runner.Pause(); _runner.Resume();
            Assert.Equal(new[] { "stop" }, calls); // pending, so no resume start
            calls.Clear(); _runner.Tick(TimeSpan.FromSeconds(59));
            Assert.Empty(calls); Assert.Empty(ramps);
            _runner.Tick(TimeSpan.FromMinutes(1));
            Assert.Equal(new[] { "start True 20" }, calls);
            Assert.Equal(20, s.BrainDrainIntensity);
            calls.Clear(); _runner.Tick(TimeSpan.FromMinutes(2.5));
            Assert.Equal(27.5, Assert.Single(ramps), 6); // ease-in at halfway
            Assert.Equal(20, s.BrainDrainIntensity); // ramp never persists to settings
            _runner.Pause(); Assert.Equal(new[] { "stop" }, calls);
            _runner.Resume(); Assert.Equal(new[] { "stop", "start True 20" }, calls);
            calls.Clear(); _runner.Tick(TimeSpan.FromMinutes(3));
            Assert.Equal(new[] { "stop" }, calls); Assert.False(s.BrainDrainEnabled);
            calls.Clear(); _runner.Pause(); _runner.Resume();
            Assert.Equal(new[] { "stop" }, calls); // ended, so no resume start
            calls.Clear(); _runner.Tick(TimeSpan.FromMinutes(3.1));
            Assert.Equal(new[] { "start True 20", "stop" }, calls); // WPF's delayed-start check repeats after end
            _runner.Stop();
            Assert.True(s.BrainDrainEnabled); Assert.Equal(73, s.BrainDrainIntensity);
        }
        finally
        {
            _runner.Stop();
            (CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain, CoreEngine.SetBrainDrainIntensity) = old;
            (s.BrainDrainEnabled, s.BrainDrainIntensity) = saved;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BrainDrain_SessionOwnsImmediateOrDisabledAudio(bool enabled)
    {
        var old = (CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain);
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, s.BrainDrainIntensity);
        var calls = new System.Collections.Generic.List<string>();
        CoreEngine.StartBrainDrain = () => calls.Add($"start {s.BrainDrainEnabled} {s.BrainDrainIntensity}");
        CoreEngine.StopBrainDrain = () => calls.Add("stop");
        try
        {
            s.BrainDrainEnabled = false; s.BrainDrainIntensity = 73;
            var session = OneMinute(); session.Settings.BrainDrainEnabled = enabled;
            session.Settings.BrainDrainStartIntensity = 25;
            _runner.Start(session);
            Assert.Equal(enabled ? new[] { "stop", "start True 25" } : new[] { "stop" }, calls); // inherited stop first
            Assert.Equal(enabled, s.BrainDrainEnabled);
            calls.Clear(); _runner.Pause(); _runner.Resume();
            Assert.Equal(enabled ? new[] { "stop", "start True 25" } : new[] { "stop" }, calls);
            calls.Clear(); _runner.Stop();
            Assert.Equal(new[] { "stop", "stop" }, calls); // session then engine teardown (WPF SessionEngine.cs:395)
            Assert.False(s.BrainDrainEnabled); Assert.Equal(73, s.BrainDrainIntensity);
        }
        finally
        {
            _runner.Stop(); (CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain) = old;
            (s.BrainDrainEnabled, s.BrainDrainIntensity) = saved;
        }
    }

    [Fact]
    public void Resume_RestartsOnlyFeaturesWhoseStartMinuteHasPassed()
    {
        int btStarts = 0;
        CoreBouncingText.StartAction = () => btStarts++;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.BouncingTextStartMinute = 1;
        session.Settings.LockCardStartMinute = 1;
        _runner.Start(session);
        _runner.Tick(TimeSpan.FromSeconds(30));
        _runner.Pause();
        btStarts = 0;

        _runner.Resume();
        Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning);   // minute 0: started before the pause
        Assert.Equal(0, btStarts);                                     // minute 1: still deferred
        Assert.False(LockCardScheduler.Instance.IsRunning);

        _runner.Tick(TimeSpan.FromSeconds(60));
        Assert.Equal(1, btStarts);
        Assert.True(LockCardScheduler.Instance.IsRunning);
    }

    [Fact]
    public void FlashRamp_FollowsElapsed_AndIsHandedBackOnStop()
    {
        var s = CoreSettings.Current;
        s.FlashOpacity = 90;
        var session = OneMinute();
        session.DurationMinutes = 2;
        session.Settings.RampCurve = RampCurve.Linear;
        session.Settings.FlashOpacity = 20; session.Settings.FlashOpacityEnd = 80;
        session.Settings.FlashPerHour = 10; session.Settings.FlashPerHourEnd = 70;
        session.Settings.FlashScale = 150;
        _runner.Start(session);

        _runner.Tick(TimeSpan.FromSeconds(30));   // 25 %
        Assert.Equal((35, 25, 150), (s.FlashOpacity, s.FlashFrequency, s.ImageScale));
        _runner.Tick(TimeSpan.FromSeconds(90));   // 75 %
        Assert.Equal((65, 55), (s.FlashOpacity, s.FlashFrequency));

        _runner.Stop();
        Assert.Equal(90, s.FlashOpacity);
    }

    [Fact]
    public void PinkTint_StartsWithinThreeMinutesOfItsMinute_ThenRamps()
    {
        var rng = new Random(7);
        var starts = Enumerable.Range(0, 2000).Select(_ => SessionRunner.RandomizedStart(true, 7, rng)).ToList();
        Assert.All(starts, m => Assert.InRange(m, 4.0, 10.0));
        Assert.True(starts.Min() < 4.1 && starts.Max() > 9.9);   // the whole window, not a narrower one
        Assert.Contains(Enumerable.Range(0, 200).Select(_ => SessionRunner.RandomizedStart(true, 1, rng)), m => m == 0);   // clamped at 0
        Assert.Equal(7, SessionRunner.RandomizedStart(false, 7, rng));
        Assert.Equal(0, SessionRunner.RandomizedStart(true, 0, rng));

        var session = OneMinute();
        session.DurationMinutes = 10;
        session.Settings.RampCurve = RampCurve.Linear;
        session.Settings.PinkFilterEnabled = true;
        session.Settings.PinkFilterStartMinute = 4;
        session.Settings.PinkFilterStartOpacity = 10;
        session.Settings.PinkFilterEndOpacity = 40;
        _runner.Start(session);
        var at = _runner.PinkStartMinute;
        Assert.InRange(at, 1.0, 7.0);
        Assert.False(CoreSettings.Current.PinkFilterEnabled);

        _runner.Tick(TimeSpan.FromMinutes(at - 0.01));
        Assert.False(CoreSettings.Current.PinkFilterEnabled);
        Assert.Null(_runner.PinkOpacity);
        _runner.Tick(TimeSpan.FromMinutes(at + 1e-4));   // TimeSpan rounds to the millisecond
        Assert.True(CoreSettings.Current.PinkFilterEnabled);
        Assert.Equal(10, _runner.PinkOpacity!.Value, 2);
        _runner.Tick(TimeSpan.FromMinutes((at + 10) / 2));   // halfway through the tint's own window
        Assert.Equal(25, _runner.PinkOpacity!.Value, 2);
    }

    [Fact]
    public void Spiral_DelayedStartLandsItsOpacity_OnlyARampingSessionHoldsIt()
    {
        var s = CoreSettings.Current;
        var saved = (s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath);
        // Default path: the shipped Spirals/spiral.gif beside the exe satisfies the guard (WPF csproj :708).
        (s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath) = (false, 33, "");
        try { SpiralRuns(s); } finally { _runner.Stop(); (s.SpiralEnabled, s.SpiralOpacity, s.SpiralPath) = saved; }
    }

    [Fact]
    public void Spiral_DelayedStartSkipsMissingFilesBeforeItsMinute()
    {
        var s = CoreSettings.Current;
        var saved = (s.SpiralEnabled, s.SpiralPath, SessionRunner.SpiralsFolder);
        var session = OneMinute();
        session.DurationMinutes = 10;
        (session.Settings.SpiralEnabled, session.Settings.SpiralStartMinute) = (true, 5);
        s.SpiralPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gif");
        SessionRunner.SpiralsFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));   // no folder
        try
        {
            _runner.Start(session);
            _runner.Tick(TimeSpan.Zero); // WPF checks files before the randomized start threshold
            Assert.False(session.Settings.SpiralEnabled);
            Assert.False(s.SpiralEnabled);
            _runner.Tick(TimeSpan.FromMinutes(9));
            Assert.False(s.SpiralEnabled);
        }
        finally { _runner.Stop(); (s.SpiralEnabled, s.SpiralPath, SessionRunner.SpiralsFolder) = saved; }
    }

    private void SpiralRuns(AppSettings s)
    {
        var session = OneMinute();
        session.DurationMinutes = 10;
        session.Settings.RampCurve = RampCurve.Linear;
        (session.Settings.SpiralEnabled, session.Settings.SpiralStartMinute) = (true, 4);
        (session.Settings.SpiralOpacity, session.Settings.SpiralOpacityEnd) = (20, 20);
        _runner.Start(session);
        var at = _runner.SpiralStartMinute;
        Assert.InRange(at, 1.0, 7.0);
        Assert.False(s.SpiralEnabled);   // SessionEngine.cs:1384: delayed stays off

        _runner.Tick(TimeSpan.FromMinutes(at - 0.01));
        Assert.False(s.SpiralEnabled);
        _runner.Tick(TimeSpan.FromMinutes(at + 1e-4));
        Assert.True(s.SpiralEnabled);
        Assert.Equal(20, s.SpiralOpacity);   // constant: lands in settings (SessionEngine.cs:845)
        Assert.Null(_runner.SpiralOpacity);  // and parks no hold (#897)
        _runner.Stop();
        Assert.False(s.SpiralEnabled);
        Assert.Equal(33, s.SpiralOpacity);   // snapshot restore

        session.Settings.SpiralStartMinute = 0;
        session.Settings.SpiralOpacityEnd = 60;
        _runner.Start(session);
        Assert.True(s.SpiralEnabled);
        Assert.Equal(20, s.SpiralOpacity);   // immediate start writes the start value
        _runner.Tick(TimeSpan.FromMinutes(5));
        Assert.Equal(40, _runner.SpiralOpacity!.Value, 2);
        Assert.Equal(20, s.SpiralOpacity);   // the ramp never writes the persisted value
        _runner.Stop();
        Assert.Null(_runner.SpiralOpacity);
    }

    private sealed class NoVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    /// <summary>WPF SessionEngine.cs:1400 (duck level), :1528-1547 (videos), :531 (pause stops them)
    /// and :580 (resume restarts them).</summary>
    [Fact]
    public void VideosAndDucking_FollowTheSession_ThroughPauseAndResume()
    {
        var previousVideo = CoreEngine.Video;
        var video = new MandatoryVideoScheduler(new NoVideoHost());
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel);
        try
        {
            CoreEngine.Video = video;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel) = (false, 3, false, 10);
            var session = OneMinute();
            session.DurationMinutes = 3;
            var ss = session.Settings;
            (ss.MandatoryVideosEnabled, ss.VideosPerHour, ss.MandatoryVideosStartMinute) = (true, 9, 1);
            ss.AudioDuckLevel = 55;

            _runner.Start(session);
            Assert.Equal((true, 9, true, 55), (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel));
            Assert.False(video.IsRunning);                              // minute 1: deferred

            _runner.Tick(TimeSpan.FromSeconds(60));
            Assert.True(video.IsRunning);

            _runner.Pause();
            Assert.False(video.IsRunning);

            _runner.Resume();
            Assert.True(video.IsRunning);

            _runner.Stop();
            Assert.False(video.IsRunning);
            Assert.Equal((false, 3, false, 10), (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel));
        }
        finally
        {
            _runner.Stop();
            video.Stop();
            CoreEngine.Video = previousVideo;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel) = saved;
        }
    }

    private sealed class NoCountHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 60;
    }

    /// <summary>WPF SessionEngine.cs:1605-1625 (schedule), :526 (pause stops it), :573 (resume restarts it
    /// unless still deferred); the stop hands the user's own values back.</summary>
    [Fact]
    public void BubbleCount_FollowsTheSession_ThroughDeferPauseResumeAndRestore()
    {
        var previous = CoreEngine.BubbleCount;
        var count = new BubbleCountScheduler(new NoCountHost(), new PopQuizSchedulerTests.FakeClock(), () => Array.Empty<string>());
        var s = CoreSettings.Current;
        var saved = (s.BubbleCountEnabled, s.BubbleCountFrequency);
        try
        {
            CoreEngine.BubbleCount = count;
            (s.BubbleCountEnabled, s.BubbleCountFrequency) = (true, 2);
            var session = OneMinute();
            session.DurationMinutes = 20;
            var ss = session.Settings;
            (ss.BubbleCountEnabled, ss.BubbleCountFrequency, ss.BubbleCountStartMinute) = (true, 4, 7);

            _runner.Start(session);
            Assert.Equal((true, 4), (s.BubbleCountEnabled, s.BubbleCountFrequency));
            Assert.False(count.IsRunning);                              // minute 7: deferred, the engine start undone
            _runner.Pause(); _runner.Resume();
            Assert.False(count.IsRunning);                              // still deferred after a resume

            _runner.Tick(TimeSpan.FromMinutes(7));
            Assert.True(count.IsRunning);
            _runner.Pause();
            Assert.False(count.IsRunning);
            _runner.Resume();
            Assert.True(count.IsRunning);

            _runner.Stop();
            Assert.False(count.IsRunning);
            Assert.Equal((true, 2), (s.BubbleCountEnabled, s.BubbleCountFrequency));

            ss.BubbleCountStartMinute = 0;
            _runner.Start(session);
            Assert.True(count.IsRunning);
            _runner.Stop();

            ss.BubbleCountEnabled = false;
            _runner.Start(session);
            Assert.False(count.IsRunning);
            Assert.False(s.BubbleCountEnabled);
        }
        finally
        {
            _runner.Stop();
            count.Stop();
            CoreEngine.BubbleCount = previous;
            (s.BubbleCountEnabled, s.BubbleCountFrequency) = saved;
        }
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public void VideoAdmissionAndPendingResumeRespectPreset(bool enabled, int minute, bool running)
    {
        var previous = CoreEngine.Video;
        var video = new MandatoryVideoScheduler(new NoVideoHost());
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel);
        try
        {
            CoreEngine.Video = video;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel) = (true, 7, true, 35);
            var session = OneMinute();
            session.DurationMinutes = 3;
            session.Settings.MandatoryVideosEnabled = enabled;
            session.Settings.MandatoryVideosStartMinute = minute;
            session.Settings.VideosPerHour = null;
            session.Settings.AudioDuckLevel = 0;
            _runner.Start(session);
            Assert.Equal(enabled, s.MandatoryVideosEnabled);
            Assert.Equal(running, video.IsRunning);
            Assert.Equal((7, true, 35), (s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel));
            _runner.Pause();
            _runner.Tick(TimeSpan.FromMinutes(2));
            Assert.False(video.IsRunning);
            _runner.Resume();
            Assert.Equal(running, video.IsRunning);
            _runner.Stop();
            Assert.False(video.IsRunning);
            _runner.Tick(TimeSpan.FromMinutes(2));
            Assert.False(video.IsRunning);
            Assert.Equal((true, 7, true, 35), (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel));
        }
        finally
        {
            _runner.Stop(); video.Stop(); CoreEngine.Video = previous;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.AudioDuckingEnabled, s.DuckingLevel) = saved;
        }
    }

    private static string Fixture([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "session_log_runner_wpf.json");
}
