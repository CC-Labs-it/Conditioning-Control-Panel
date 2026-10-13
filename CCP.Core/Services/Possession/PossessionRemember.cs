using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  "IT REMEMBERS" - the haunt's one line of memory. Read Services/Possession/POSSESSION.md first.
//
//  Wave 2, item C2. Everything else Possession does is bounded by the lockdown timer: the room misbehaves,
//  the timer ends, the room reassembles, and the whole thing is over. That containment is what makes the
//  feature safe, and it is also what makes it forgettable. So exactly ONE thing survives the timer, and
//  only from a Full Doki run: about twenty seconds into the NEXT launch, the Lockdown door takes a single
//  ember charge and the companion says one line. Nothing moves, nothing is haunted, no effect starts. It
//  is a look, not a haunt.
//
//  RULES that keep it from becoming a haunt outside a lockdown:
//    - Full Doki only, read at DEACTIVATE time (the intensity dial may be turned down before next launch;
//      what matters is the intensity of the run the user actually lived through).
//    - Once. The flag is cleared the moment this service starts, BEFORE anything is scheduled, so a crash,
//      a kill or a second instance can never leave it stuck on and charging every launch forever.
//    - Never while a lockdown is running (crash recovery can hand us one), never without a tube, never
//      without a visible door to charge.
//
//  This file also owns the per-lockdown escape counter, because it is the one thing that already lives
//  across the whole lockdown lifecycle and nothing else exposes it: LockdownService counts escapes into a
//  private field and publishes the total only on the EscapeAttempted argument. The companion's chat prompt
//  (PromptAssembler, wave-2 item B12) reads it from here so the warden knows how many times they pulled at
//  the door.
//
//  Head-neutral since possession-core-c: the head says when its main window is on screen and spends the
//  charge (WPF: PossessionRememberCharge - the ember ripple on the Lockdown door, the tube check, the
//  line). The rules, the flag and the clock live here.
// =====================================================================================================

/// <summary>The "it remembers" charge and the per-lockdown escape counter. See the file header.</summary>
public static class PossessionRemember
{
    /// <summary>How long after the main window is up the charge lands.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>Give up if the main window never shows (a headless/preview run, an early crash).</summary>
    private static readonly TimeSpan WaitForWindowTimeout = TimeSpan.FromMinutes(3);

    private static bool _installed;
    private static bool _spent;
    private static bool _waiting;
    private static int _escapeAttempts;
    private static Timer? _timer;
    private static Func<bool> _isWindowReady = () => false;
    private static Action _fire = () => { };

    /// <summary>Test seams: the clock, and whether a real one-second timer is started.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static bool TimerEnabled = true;
    private static DateTime _startedAt;
    private static DateTime _windowReadyAt = DateTime.MinValue;
    private static LockdownService? _lockdown;

    /// <summary>
    /// How many escape attempts the CURRENT lockdown has seen, all kinds combined. Zero when no lockdown
    /// is running. Read by the companion prompt so the warden can tease them about it.
    /// </summary>
    public static int EscapeAttempts => Volatile.Read(ref _escapeAttempts);

    /// <summary>
    /// Wire up the lockdown lifecycle and, if the last Full Doki run armed it, schedule this launch's one
    /// charge. Called once from App.OnStartup next to the director. Safe to call twice.
    /// <paramref name="isWindowReady"/>: the main window is loaded and visible. <paramref name="fire"/>:
    /// the head's charge, run on its UI thread, never while a lockdown runs.
    /// </summary>
    public static void Install(LockdownService? lockdown, Func<bool> isWindowReady, Action fire)
    {
        if (_installed) return;
        _installed = true;
        _isWindowReady = isWindowReady;
        _fire = fire;

        try
        {
            if (lockdown != null)
            {
                lockdown.LockdownActivated += OnLockdownActivated;
                lockdown.LockdownDeactivated += OnLockdownDeactivated;
                lockdown.EscapeAttempted += OnEscapeAttempted;
            }
        }
        catch (Exception ex) { Log.Warning("PossessionRemember install failed: {Error}", ex.Message); }
        _lockdown = lockdown;

        try { SchedulePendingCharge(); }
        catch (Exception ex) { Log.Warning("PossessionRemember schedule failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  Lockdown lifecycle
    // -------------------------------------------------------------------------------------------

    private static void OnLockdownActivated() => Interlocked.Exchange(ref _escapeAttempts, 0);

    private static void OnEscapeAttempted(EscapeAttempt attempt)
    {
        // Total, not Repeat: the prompt wants "they have tried K times", across every kind.
        Volatile.Write(ref _escapeAttempts, attempt.Total);
    }

    private static void OnLockdownDeactivated()
    {
        try
        {
            Interlocked.Exchange(ref _escapeAttempts, 0);

            var settings = CoreSettings.Current;

            // Read the intensity HERE, at the end of the run that earned it. Reading it next launch
            // instead would let a user who dialled back to Eerie in the meantime get a memory of a
            // run they never had - and, worse, hide one they did.
            if (settings.LockdownPossessionIntensity != (int)PossessionIntensity.FullDoki) return;
            if (settings.LockdownPossessionEnabled != true) return;

            settings.LockdownPossessionRememberPending = true;
            Log.Debug("PossessionRemember armed (Full Doki lockdown ended)");
        }
        catch (Exception ex) { Log.Warning("PossessionRemember arm failed: {Error}", ex.Message); }
    }

    // -------------------------------------------------------------------------------------------
    //  The one charge
    // -------------------------------------------------------------------------------------------

    internal static void SchedulePendingCharge()
    {
        var settings = CoreSettings.Current;
        if (!settings.LockdownPossessionRememberPending) return;

        // FAILSAFE, and the reason it is the very first thing that happens: the flag is spent the moment
        // we read it, not when the charge lands. Everything below can fail, be cancelled, or never get a
        // window - and the next launch still starts clean.
        settings.LockdownPossessionRememberPending = false;

        if (settings.LockdownPossessionEnabled != true) return;

        _startedAt = UtcNow();
        _windowReadyAt = DateTime.MinValue;
        _waiting = true;
        try
        {
            _timer?.Dispose();
            // Ticks on the head's UI thread, as LockdownService's countdown does.
            if (TimerEnabled)
                _timer = new Timer(_ => CoreDispatch.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) { Log.Warning("PossessionRemember timer failed: {Error}", ex.Message); }
    }

    /// <summary>True while a remembered charge waits for the window.</summary>
    internal static bool IsWaiting => _waiting;

    internal static void Tick()
    {
        if (!_waiting) return;
        try
        {
            if (_spent) { Stop(); return; }
            if (UtcNow() - _startedAt > WaitForWindowTimeout) { Stop(); return; }

            if (!_isWindowReady())
            {
                // The clock only starts once the room is actually on screen: twenty seconds of a
                // splash screen is not twenty seconds of someone sitting there.
                _windowReadyAt = DateTime.MinValue;
                return;
            }

            if (_windowReadyAt == DateTime.MinValue) _windowReadyAt = UtcNow();
            if (UtcNow() - _windowReadyAt < Delay) return;

            Stop();
            if (_spent) return;
            _spent = true;
            Fire();
        }
        catch (Exception ex)
        {
            Log.Warning("PossessionRemember tick failed: {Error}", ex.Message);
            Stop();
        }
    }

    private static void Stop()
    {
        _waiting = false;
        try { _timer?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        _timer = null;
    }

    private static void Fire()
    {
        try
        {
            // A lockdown that started while we were counting owns the room now; a memory of the LAST one
            // on top of a live haunt is just noise.
            if (_lockdown?.IsActive == true) return;

            _fire();
        }
        catch (Exception ex) { Log.Warning("PossessionRemember fire failed: {Error}", ex.Message); }
    }

    /// <summary>Tests only: unhook and forget everything so the next Install starts clean.</summary>
    internal static void ResetForTests()
    {
        Stop();
        if (_lockdown != null)
        {
            _lockdown.LockdownActivated -= OnLockdownActivated;
            _lockdown.LockdownDeactivated -= OnLockdownDeactivated;
            _lockdown.EscapeAttempted -= OnEscapeAttempted;
        }
        _lockdown = null;
        _installed = _spent = false;
        _escapeAttempts = 0;
        _windowReadyAt = DateTime.MinValue;
        _isWindowReady = () => false;
        _fire = () => { };
    }
}
