using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Takeover's schedule on a fake clock (Core AutonomyScheduler, WPF AutonomyService rules).</summary>
[Collection(SessionStatics.Name)]
public class AutonomySchedulerTests : IDisposable
{
    private DateTime _now = new(2026, 1, 1, 14, 0, 0);   // afternoon: Attentive, no mood skew
    private readonly List<AutonomyActionType> _performed = new();

    public AutonomySchedulerTests()
    {
        Reset();
        var s = CoreSettings.Current;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = s.AutonomyRandomTriggerEnabled = s.AutonomyCanTriggerFlash = true;
        s.AutonomyRandomIntervalSeconds = 60;
        s.AutonomyCooldownSeconds = 30;
        s.AutonomyAnnouncementChance = 0;
        CoreEntitlement.HasPremiumProvider = () => true;
    }

    public void Dispose()
    {
        CoreEntitlement.HasPremiumProvider = null;
        Reset();
    }

    private static void Reset()
    {
        var s = CoreSettings.Current;
        var d = new AppSettings();
        s.AutonomyModeEnabled = d.AutonomyModeEnabled;
        s.AutonomyConsentGiven = d.AutonomyConsentGiven;
        s.AutonomyRandomTriggerEnabled = d.AutonomyRandomTriggerEnabled;
        s.AutonomyIdleTriggerEnabled = false;
        s.AutonomyIdleTimeoutMinutes = d.AutonomyIdleTimeoutMinutes;
        s.AutonomyTimeAwareEnabled = false;
        s.AutonomyRandomIntervalSeconds = d.AutonomyRandomIntervalSeconds;
        s.AutonomyCooldownSeconds = d.AutonomyCooldownSeconds;
        s.AutonomyAnnouncementChance = d.AutonomyAnnouncementChance;
        s.AutonomyCanTriggerFlash = d.AutonomyCanTriggerFlash;
        s.AutonomyCanTriggerMindWipe = false;
        s.AutonomyCanTriggerVideo = s.AutonomyCanTriggerSubliminal = s.AutonomyCanTriggerBubbles = false;
        s.AutonomyCanComment = s.AutonomyCanTriggerLockCard = s.AutonomyCanTriggerPinkFilter = false;
        s.AutonomyCanTriggerBouncingText = s.AutonomyCanTriggerWebVideo = s.AutonomyCanTriggerWallpaper = false;
        s.AutonomyCanTriggerVoiceCommand = false;
    }

    private AutonomyScheduler Make() => new()
    {
        Clock = () => _now, Rng = new Random(1),
        CanPerform = a => a == AutonomyActionType.Flash,
        Perform = (a, _) => _performed.Add(a),
    };

    private void Advance(AutonomyScheduler s, int seconds)
    {
        for (var i = 0; i < seconds; i++) { _now = _now.AddSeconds(1); s.Tick(); }
    }

    [Fact]
    public void RandomTriggerFiresWithinTheJitteredInterval()
    {
        var s = Make();
        Assert.True(s.Start(withTimer: false));
        Advance(s, 39);
        Assert.Empty(_performed);   // 60 s ±33 % never fires before 40 s
        Advance(s, 41);
        Assert.Equal(new[] { AutonomyActionType.Flash }, _performed);
    }

    [Fact]
    public void NothingArmsWithoutEnableConsentAndEntitlement()
    {
        CoreSettings.Current.AutonomyConsentGiven = false;
        Assert.False(Make().Start(withTimer: false));
        CoreSettings.Current.AutonomyConsentGiven = true;
        CoreEntitlement.HasPremiumProvider = () => false;
        Assert.False(Make().Start(withTimer: false));
        CoreEntitlement.IsFreeTodayProvider = k => k == "takeover";   // the ? box's free day
        try { Assert.True(Make().Start(withTimer: false)); }
        finally { CoreEntitlement.IsFreeTodayProvider = null; }
    }

    [Fact]
    public void LapsedEntitlementStopsHerActing()
    {
        var s = Make();
        s.Start(withTimer: false);
        CoreEntitlement.HasPremiumProvider = () => false;
        Advance(s, 200);
        Assert.Empty(_performed);
    }

    [Fact]
    public void BusyAndCooldownDeferToAShortRetry()
    {
        var s = Make();
        var busy = true;
        s.IsBusy = () => busy;
        s.Start(withTimer: false);
        Advance(s, 80);
        Assert.Empty(_performed);
        busy = false;
        Advance(s, 24);   // retry window is 12-24 s
        Assert.Single(_performed);
    }

    [Fact]
    public void StopDropsTheAnnouncedAction()
    {
        CoreSettings.Current.AutonomyAnnouncementChance = 100;
        var s = Make();
        string? said = null;
        s.AnnouncementMade += (_, p) => said = p;
        s.Start(withTimer: false);
        for (var i = 0; i < 100 && said == null; i++) Advance(s, 1);
        Assert.NotNull(said);
        Assert.Empty(_performed);   // 2 s announce delay
        s.Stop();                   // panic / Stop
        Advance(s, 1);
        Assert.False(s.IsEnabled);
        // Re-armed inside the 2 s window: the old takeover's queued action must still be dropped (#1153).
        CoreSettings.Current.AutonomyAnnouncementChance = 0;
        Assert.True(s.Start(withTimer: false));
        Advance(s, 5);
        Assert.Empty(_performed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MindWipeDisabledDuringAnnouncementDoesNotRun(bool disableCard)
    {
        var st = CoreSettings.Current;
        st.AutonomyCanTriggerFlash = false;
        st.AutonomyCanTriggerMindWipe = st.MindWipeEnabled = true;
        st.AutonomyAnnouncementChance = 100;
        var s = Make();
        s.CanPerform = a => a == AutonomyActionType.MindWipe;
        try
        {
            s.Start(withTimer: false);
            s.TestTrigger();
            if (disableCard) st.MindWipeEnabled = false;
            else st.AutonomyCanTriggerMindWipe = false;
            Advance(s, 2);
            Assert.Empty(_performed);
        }
        finally { st.MindWipeEnabled = false; s.Stop(); }
    }

    [Fact]
    public void CountdownTracksCurrentIntervalRetryRefreshAndStop()
    {
        var s = Make();
        Assert.Null(s.NextRandomFireFraction);
        s.Start(withTimer: false);
        var start = _now;
        var interval = (s.NextRandomFire - start).TotalSeconds;
        Assert.Equal(1, s.NextRandomFireFraction!.Value);
        _now = start.AddSeconds(interval / 2);
        Assert.Equal(0.5, s.NextRandomFireFraction!.Value, 6);
        _now = start.AddSeconds(-10);
        Assert.Equal(1, s.NextRandomFireFraction!.Value);
        _now = start.AddSeconds(interval + 1);
        Assert.Equal(0, s.NextRandomFireFraction!.Value);
        s.IsBusy = () => true;
        s.Tick();
        Assert.InRange((s.NextRandomFire - _now).TotalSeconds, 12, 24);
        Assert.Equal(1, s.NextRandomFireFraction!.Value);
        CoreSettings.Current.AutonomyRandomTriggerEnabled = false;
        s.RefreshRandomTimer();
        Assert.Null(s.NextRandomFireFraction);
        CoreSettings.Current.AutonomyRandomTriggerEnabled = true;
        s.RefreshRandomTimer();
        Assert.Equal(1, s.NextRandomFireFraction!.Value);
        s.Stop();
        Assert.Null(s.NextRandomFireFraction);
    }

    [Fact]
    public void DiagnosticStatusReportsLiveTimersCooldownAndLastActionInWpfOrder()
    {
        var s = Make();
        Assert.StartsWith("Service Enabled: False\nIdle Timer Running: False\nRandom Timer Running: False", s.GetDiagnosticStatus());
        Assert.Contains("Last Action: Never\nCurrent Mood: Attentive\n\nSettings:", s.GetDiagnosticStatus());
        s.Start(withTimer: false);
        s.TestTrigger();
        s.IsBusy = () => true;
        Assert.StartsWith("Service Enabled: True\nIdle Timer Running: False\nRandom Timer Running: True\nOn Cooldown: True\nInteraction Queue Busy: True\nLast Action: 14:00:00", s.GetDiagnosticStatus());
        _now = _now.AddSeconds(31);
        Assert.Contains("On Cooldown: False", s.GetDiagnosticStatus());
        Assert.EndsWith("  PinkFilter: False\n  BouncingText: False", s.GetDiagnosticStatus());
        s.Stop();
        Assert.Contains("Random Timer Running: False", s.GetDiagnosticStatus());
    }

    /// <summary>A Random whose rolls are fixed, so the weighted pick is exact.</summary>
    private sealed class FixedRoll : Random
    {
        private readonly int _roll;
        public FixedRoll(int roll) => _roll = roll;
        public override int Next(int maxValue) => _roll;
    }

    [Theory]
    // Gentle: Video 15*0.5 = 7, Comment 20*1.5 = 30.
    [InlineData(AutonomyMood.Gentle, 5, 6, AutonomyActionType.Video)]
    [InlineData(AutonomyMood.Gentle, 5, 7, AutonomyActionType.Comment)]
    // Attentive, intensity 10: Video 15*(1+0.5) = 22, Comment 20.
    [InlineData(AutonomyMood.Attentive, 10, 21, AutonomyActionType.Video)]
    [InlineData(AutonomyMood.Attentive, 10, 22, AutonomyActionType.Comment)]
    // Mischievous, intensity 1: Video 15*1.5 = 22, then *(1-0.4) = 13.
    [InlineData(AutonomyMood.Mischievous, 1, 12, AutonomyActionType.Video)]
    [InlineData(AutonomyMood.Mischievous, 1, 13, AutonomyActionType.Comment)]
    public void WeightedPickFollowsMoodAndIntensity(AutonomyMood mood, int intensity, int roll, AutonomyActionType expected)
    {
        var candidates = new List<(AutonomyActionType, int)> { (AutonomyActionType.Video, 15), (AutonomyActionType.Comment, 20) };
        Assert.Equal(expected, AutonomyScheduler.Pick(candidates, mood, intensity, new FixedRoll(roll)));
    }

    [Fact]
    public void IdleTriggerFiresAfterTheTimeoutAndActivityResetsIt()
    {
        var st = CoreSettings.Current;
        st.AutonomyRandomTriggerEnabled = false;
        st.AutonomyIdleTriggerEnabled = true;
        st.AutonomyIdleTimeoutMinutes = 1;
        var s = Make();
        s.Start(withTimer: false);
        Advance(s, 50);
        s.ReportUserActivity();
        Advance(s, 59);
        Assert.Empty(_performed);   // 109 s since start, 59 s since activity
        Advance(s, 1);
        Assert.Single(_performed);
        Advance(s, 60);             // the idle timer repeats like WPF's DispatcherTimer
        Assert.Equal(2, _performed.Count);
    }

    [Fact]
    public void UnperformableActionsAreNeverPicked()
    {
        CoreSettings.Current.AutonomyCanTriggerFlash = false;
        CoreSettings.Current.AutonomyCanTriggerMindWipe = true;   // not on this head
        var s = Make();
        s.Start(withTimer: false);
        Advance(s, 300);
        Assert.Empty(_performed);
    }

    [Fact]
    public void IntervalRulesMatchWpf()
    {
        var st = new AppSettings { AutonomyRandomIntervalSeconds = 60, AutonomyTimeAwareEnabled = true, AutonomyNightMultiplier = 2 };
        Assert.Equal(20, AutonomyScheduler.NextRandomSeconds(st, false, false, 23, 0.0), 3);   // 40 s / 2 at night
        Assert.Equal(24, AutonomyScheduler.NextRandomSeconds(st, true, false, 23, 1.0), 3);    // retry ceiling
        Assert.Equal(AutonomyMood.Mischievous, AutonomyScheduler.MoodAt(3));
        Assert.Equal(AutonomyMood.Gentle, AutonomyScheduler.MoodAt(8));
    }
}
