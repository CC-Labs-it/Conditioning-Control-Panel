using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartEngine / StopEngine arming matrix for the Core-driven features, and the
// card rule: a toggle only saves while stopped and applies live while running.
[Collection(SessionStatics.Name)]
public sealed class CoreEngineTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]   // #668 audio-only: flags On, nothing visual starts
    public void Start_arms_by_saved_flags_and_Stop_disarms_without_touching_them(bool flags, bool audioOnly)
    {
        var s = CoreSettings.Current;
        s.AudioOnlySession = audioOnly;
        var on = flags && !audioOnly;
        s.FlashEnabled = s.SubliminalEnabled = s.LockCardEnabled = s.BouncingTextEnabled = flags;
        int btStart = 0, btStop = 0, hook = 0;
        CoreBouncingText.StartAction = () => btStart++;
        CoreBouncingText.StopAction = () => btStop++;
        CoreEngine.StoppedHook = () => hook++;
        var sessions = s.TotalSessions;
        try
        {
            CoreEngine.Start();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(!audioOnly, CoreFlash.IsRunning);   // started unless audio-only; it gates on FlashEnabled itself
            Assert.Equal(on, CoreSubliminal.IsRunning);
            Assert.Equal(on, LockCardScheduler.Instance.IsRunning);
            Assert.Equal((on ? 1 : 0, on ? 0 : 1), (btStart, btStop));
            Assert.Equal(sessions + 1, s.TotalSessions);

            var stopsBefore = btStop;
            CoreEngine.Stop();
            Assert.False(CoreEngine.IsRunning || CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            Assert.Equal(1, hook);
            Assert.Equal(stopsBefore + 1, btStop);
            Assert.Equal(flags, s.FlashEnabled && s.SubliminalEnabled && s.LockCardEnabled && s.BouncingTextEnabled);
        }
        finally { s.AudioOnlySession = false; CoreEngine.Stop(); CoreEngine.StoppedHook = null; CoreBouncingText.StartAction = CoreBouncingText.StopAction = null; }
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    public void Start_counts_only_new_user_sessions(bool systemInitiated, bool resumingSession, int increment)
    {
        var s = CoreSettings.Current;
        var before = s.TotalSessions;
        try
        {
            CoreEngine.Start(resumingSession: resumingSession, systemInitiated: systemInitiated);
            Assert.Equal(before + increment, s.TotalSessions);
        }
        finally { CoreEngine.Stop(); s.TotalSessions = before; }
    }

    // WPF MainWindow.StartStop.cs:449 / :592-601: EMI hears the start origin, then the whole minutes run.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Start_and_Stop_raise_EMI_moments_with_origin_and_minutes(bool systemInitiated)
    {
        var s = CoreSettings.Current;
        var before = s.TotalSessions;
        var clock = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var oldClock = CoreEngine.UtcNow;
        var fired = new List<(string Id, IReadOnlyDictionary<string, object?> Ctx)>();
        bool hookRanFirst = false;
        CoreEngine.UtcNow = () => clock;
        CoreEngine.StoppedHook = () => hookRanFirst = fired.Count == 1;
        CoreEngine.MomentHook = (id, ctx) => fired.Add((id, ConditioningControlPanel.Services.EmiDesk.EmiLineEngine.ToCtx(ctx)));
        try
        {
            CoreEngine.Start(systemInitiated: systemInitiated);
            Assert.Equal("engineStarted", Assert.Single(fired).Id);
            Assert.Equal((object)systemInitiated, fired[0].Ctx["systemInitiated"]);

            clock = clock.AddMinutes(7).AddSeconds(59);
            CoreEngine.Stop();
            Assert.True(hookRanFirst);   // after the head's teardown, as WPF fires after UpdateStartButton
            Assert.Equal("engineStopped", fired[1].Id);
            Assert.Equal((object)7, fired[1].Ctx["minutes"]);

            CoreEngine.Stop();   // nothing running: zero, never the old run again
            Assert.Equal("engineStopped", fired[2].Id);
            Assert.Equal((object)0, fired[2].Ctx["minutes"]);
        }
        finally
        {
            CoreEngine.Stop();
            CoreEngine.MomentHook = null; CoreEngine.StoppedHook = null; CoreEngine.UtcNow = oldClock;
            s.TotalSessions = before;
        }
    }

    [Fact]
    public void Toggles_do_nothing_while_stopped_and_apply_live_while_running()
    {
        var s = CoreSettings.Current;
        s.FlashEnabled = s.SubliminalEnabled = s.LockCardEnabled = s.BouncingTextEnabled = false;
        int btStart = 0;
        CoreBouncingText.StartAction = () => btStart++;
        CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
        try
        {
            // Stopped: flags save, nothing arms.
            s.FlashEnabled = s.LockCardEnabled = s.BouncingTextEnabled = true;
            foreach (var k in new[] { "flash", "lockcard", "bouncingtext" }) CoreEngine.ApplyLive(k, true);
            CoreSubliminal.SetEnabled(true);
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            Assert.Equal(0, btStart);
            Assert.True(s.SubliminalEnabled);

            // Running: the same toggles off then on apply at once.
            CoreEngine.Start();
            CoreSubliminal.SetEnabled(false);
            CoreEngine.ApplyLive("flash", false);
            CoreEngine.ApplyLive("lockcard", false);
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            CoreSubliminal.SetEnabled(true);
            CoreEngine.ApplyLive("flash", true);
            CoreEngine.ApplyLive("lockcard", true);
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && LockCardScheduler.Instance.IsRunning);
        }
        finally
        {
            CoreEngine.Stop();
            CoreSession.IsEngineRunningProvider = null;
            CoreBouncingText.StartAction = null;
        }
    }

    // #872: a preset load stops what it switched off and never starts what it switched on.
    [Fact]
    public void Reconcile_stops_cleared_features_and_starts_nothing()
    {
        var s = CoreSettings.Current;
        s.FlashEnabled = s.LockCardEnabled = true;
        s.SubliminalEnabled = s.BouncingTextEnabled = false;
        try
        {
            CoreEngine.Start();
            Assert.True(CoreFlash.IsRunning && LockCardScheduler.Instance.IsRunning);
            s.FlashEnabled = false;          // the preset cleared flash...
            s.SubliminalEnabled = true;      // ...and set subliminal
            CoreEngine.Reconcile();
            Assert.False(CoreFlash.IsRunning);
            Assert.True(LockCardScheduler.Instance.IsRunning);
            Assert.False(CoreSubliminal.IsRunning);
        }
        finally { CoreEngine.Stop(); }
    }
}
