using System;
using System.Threading;
using ConditioningControlPanel.Models;
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
// =====================================================================================================

/// <summary>The "it remembers" charge and the per-lockdown escape counter. See the file header.</summary>
public static class PossessionRemember
{
    /// <summary>Bark trigger for the remembered line. One text-only variant per mod pack.</summary>
    public const string RememberTrigger = "PossessionRemember";

    /// <summary>How long after the main window is up the charge lands.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>Give up if the main window never shows (a headless/preview run, an early crash).</summary>
    private static readonly TimeSpan WaitForWindowTimeout = TimeSpan.FromMinutes(3);

    private static bool _installed;
    private static bool _spent;
    private static int _escapeAttempts;
    private static IDisposable? _timer;
    private static LockdownService? _lockdown;
    private static DateTime _startedAt;
    private static DateTime _windowReadyAt = DateTime.MinValue;

    /// <summary>
    /// How many escape attempts the CURRENT lockdown has seen, all kinds combined. Zero when no lockdown
    /// is running. Read by the companion prompt so the warden can tease them about it.
    /// </summary>
    public static int EscapeAttempts => Volatile.Read(ref _escapeAttempts);

    // ---- The head's half (WPF seeds these in App before Install) ----------------------------------
    public static Func<AppSettings?> Settings { get; set; } = () => CoreSettings.Service?.Current;
    /// <summary>Start a repeating UI timer (WPF: a Background DispatcherTimer); disposing stops it.</summary>
    public static Func<TimeSpan, Action, IDisposable?> StartTimer { get; set; } = (_, _) => null;
    /// <summary>The main window is loaded and visible (the twenty seconds count only from then).</summary>
    public static Func<bool> WindowReady { get; set; } = () => false;
    /// <summary>The companion's tube exists (no tube, no line, no point).</summary>
    public static Func<bool> HasCompanion { get; set; } = () => false;
    /// <summary>One ember charge on the visible Lockdown door; false when there is no door to charge.</summary>
    public static Func<bool> ChargeDoor { get; set; } = () => false;
    /// <summary>Say "i remember." (bark pack, else the spoken-bubble fallback).</summary>
    public static Action SayRemembered { get; set; } = () => { };
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// Wire up the lockdown lifecycle and, if the last Full Doki run armed it, schedule this launch's one
    /// charge. Called once from App.OnStartup next to the director. Safe to call twice.
    /// </summary>
    public static void Install(LockdownService? lockdown)
    {
        if (_installed) return;
        _installed = true;
        _lockdown = lockdown;

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

    internal static void OnLockdownDeactivated()
    {
        try
        {
            Interlocked.Exchange(ref _escapeAttempts, 0);

            var settings = Settings();
            if (settings == null) return;

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
        var settings = Settings();
        if (settings == null) return;
        if (!settings.LockdownPossessionRememberPending) return;

        // FAILSAFE, and the reason it is the very first thing that happens: the flag is spent the moment
        // we read it, not when the charge lands. Everything below can fail, be cancelled, or never get a
        // window - and the next launch still starts clean.
        settings.LockdownPossessionRememberPending = false;

        if (settings.LockdownPossessionEnabled != true) return;

        _startedAt = UtcNow();
        try
        {
            Stop();
            _timer = StartTimer(TimeSpan.FromSeconds(1), Tick);
        }
        catch (Exception ex) { Log.Warning("PossessionRemember timer failed: {Error}", ex.Message); }
    }

    internal static void Tick()
    {
        try
        {
            if (_spent) { Stop(); return; }
            if (UtcNow() - _startedAt > WaitForWindowTimeout) { Stop(); return; }

            if (!WindowReady())
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

    /// <summary>Tests only: forget the once-per-launch state.</summary>
    internal static void ResetForTests()
    {
        Stop();
        _installed = false;
        _spent = false;
        _lockdown = null;
        _windowReadyAt = DateTime.MinValue;
    }

    private static void Stop()
    {
        try { _timer?.Dispose(); } catch { }
        _timer = null;
    }

    private static void Fire()
    {
        try
        {
            // A lockdown that started while we were counting owns the room now; a memory of the LAST one
            // on top of a live haunt is just noise.
            if (_lockdown?.IsActive == true) return;

            // No tube, no line, no point: the charge alone reads as a glitch rather than a message.
            if (!HasCompanion()) return;

            // The head resolves the visible door (nav rail Lockdown entry, else the Play door) and
            // charges it once; no door, no line.
            if (!ChargeDoor()) return;

            SayRemembered();
        }
        catch (Exception ex) { Log.Warning("PossessionRemember fire failed: {Error}", ex.Message); }
    }
}
