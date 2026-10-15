using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// "It remembers" (POSSESSION.md C2), moved from the WPF head into Core: only a Full Doki lockdown
/// arms the charge, the flag is spent the moment the next launch reads it, the charge lands once,
/// 20 s after the window is on screen and never while a lockdown runs, and the escape counter the
/// companion prompt reads follows the running lockdown.
/// </summary>
[Collection(SessionStatics.Name)]
public sealed class PossessionRememberTests : IDisposable
{
    private readonly AppSettings _s = CoreSettings.Current;
    private readonly (bool, int, bool, bool, bool, bool, bool, bool) _saved;
    private readonly LockdownService _lockdown = new();
    private DateTime _now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private bool _windowReady;
    private int _fired;

    private readonly (Func<AppSettings?>, Func<TimeSpan, Action, IDisposable?>, Func<bool>, Func<bool>, Func<bool>, Action, Func<DateTime>) _seams;
    private Action? _tick;

    public PossessionRememberTests()
    {
        _saved = (_s.LockdownPossessionEnabled, _s.LockdownPossessionIntensity, _s.LockdownPossessionRememberPending,
            _s.LockdownTripwiresEnabled, _s.StrictLockEnabled, _s.PanicKeyEnabled, _s.LockdownForceStrictLock, _s.LockdownDisablePanicKey);
        _s.LockdownForceStrictLock = _s.LockdownDisablePanicKey = false;
        _seams = (PossessionRemember.Settings, PossessionRemember.StartTimer, PossessionRemember.WindowReady,
            PossessionRemember.HasCompanion, PossessionRemember.ChargeDoor, PossessionRemember.SayRemembered, PossessionRemember.UtcNow);
        PossessionRemember.ResetForTests();
        PossessionRemember.Settings = () => _s;
        PossessionRemember.UtcNow = () => _now;
        // The head's UI timer: Tick runs only while it is alive, as a disposed DispatcherTimer stops.
        PossessionRemember.StartTimer = (_, t) => { _tick = t; return new StopTimer(this); };
        PossessionRemember.WindowReady = () => _windowReady;
        PossessionRemember.HasCompanion = () => true;
        PossessionRemember.ChargeDoor = () => { _fired++; return true; };
        PossessionRemember.SayRemembered = () => { };
    }

    private sealed class StopTimer(PossessionRememberTests t) : IDisposable
    {
        public void Dispose() => t._tick = null;
    }

    private bool IsWaiting => _tick != null;

    public void Dispose()
    {
        _lockdown.Deactivate();
        _lockdown.Dispose();
        PossessionRemember.ResetForTests();
        (PossessionRemember.Settings, PossessionRemember.StartTimer, PossessionRemember.WindowReady,
            PossessionRemember.HasCompanion, PossessionRemember.ChargeDoor, PossessionRemember.SayRemembered, PossessionRemember.UtcNow) = _seams;
        (_s.LockdownPossessionEnabled, _s.LockdownPossessionIntensity, _s.LockdownPossessionRememberPending,
            _s.LockdownTripwiresEnabled, _s.StrictLockEnabled, _s.PanicKeyEnabled, _s.LockdownForceStrictLock, _s.LockdownDisablePanicKey) = _saved;
    }

    private void Install() => PossessionRemember.Install(_lockdown);

    private void Advance(double seconds)
    {
        _now = _now.AddSeconds(seconds);
        _tick?.Invoke();
    }

    [Theory]
    [InlineData(PossessionIntensity.FullDoki, true, true)]
    [InlineData(PossessionIntensity.Eerie, true, false)]
    [InlineData(PossessionIntensity.Gentle, true, false)]
    [InlineData(PossessionIntensity.FullDoki, false, false)]
    public void Only_a_Full_Doki_lockdown_arms_the_charge_read_at_its_end(PossessionIntensity atEnd, bool enabled, bool armed)
    {
        _s.LockdownPossessionRememberPending = false;
        _s.LockdownPossessionEnabled = enabled;
        _s.LockdownPossessionIntensity = (int)PossessionIntensity.Gentle;   // what it was at Start does not matter
        Install();

        _lockdown.Activate(TimeSpan.FromMinutes(10));
        _s.LockdownPossessionIntensity = (int)atEnd;
        _lockdown.Deactivate();

        Assert.Equal(armed, _s.LockdownPossessionRememberPending);
    }

    [Fact]
    public void The_flag_is_spent_at_startup_even_when_Possession_was_switched_off_since()
    {
        _s.LockdownPossessionRememberPending = true;
        _s.LockdownPossessionEnabled = false;
        Install();

        Assert.False(_s.LockdownPossessionRememberPending);
        Assert.False(IsWaiting);
        _windowReady = true;
        Advance(30);
        Assert.Equal(0, _fired);
    }

    [Fact]
    public void The_charge_lands_once_20s_after_the_window_is_on_screen()
    {
        _s.LockdownPossessionRememberPending = true;
        _s.LockdownPossessionEnabled = true;
        Install();
        Assert.False(_s.LockdownPossessionRememberPending);   // spent before anything is shown

        Advance(30);                                          // splash screen: the clock has not started
        _windowReady = true;
        Advance(1);                                           // window up: clock starts here
        Advance(15);
        _windowReady = false;                                 // hidden again: the clock resets
        Advance(1);
        _windowReady = true;
        Advance(1);
        Advance(19);
        Assert.Equal(0, _fired);
        Advance(1);
        Assert.Equal(1, _fired);
        Assert.False(IsWaiting);

        Advance(30);
        Assert.Equal(1, _fired);                              // once
    }

    [Fact]
    public void A_running_lockdown_swallows_the_charge()
    {
        _s.LockdownPossessionRememberPending = true;
        _s.LockdownPossessionEnabled = true;
        _s.LockdownPossessionIntensity = (int)PossessionIntensity.Eerie;
        Install();
        _windowReady = true;
        Advance(1);
        _lockdown.Activate(TimeSpan.FromMinutes(10));
        Advance(20);

        Assert.Equal(0, _fired);
        Assert.False(IsWaiting);           // spent, not postponed
    }

    [Fact]
    public void The_wait_gives_up_after_three_minutes_without_a_window()
    {
        _s.LockdownPossessionRememberPending = true;
        _s.LockdownPossessionEnabled = true;
        Install();
        Advance(181);
        Assert.False(IsWaiting);
        _windowReady = true;
        Advance(30);
        Assert.Equal(0, _fired);
    }

    [Fact]
    public void Escape_attempts_follow_the_running_lockdown()
    {
        _s.LockdownTripwiresEnabled = true;
        _s.LockdownPossessionEnabled = false;
        Install();

        _lockdown.Activate(TimeSpan.FromMinutes(10));
        _lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
        _lockdown.NotifyEscapeAttempt(EscapeKinds.Stop);
        _lockdown.NotifyEscapeAttempt(EscapeKinds.Close);
        Assert.Equal(3, PossessionRemember.EscapeAttempts);   // total across kinds, not one kind's repeat

        _lockdown.Deactivate();
        Assert.Equal(0, PossessionRemember.EscapeAttempts);
    }
}
