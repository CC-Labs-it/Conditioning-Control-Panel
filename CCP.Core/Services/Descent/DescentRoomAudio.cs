using System;
using Serilog;

namespace ConditioningControlPanel.Services.Descent;

public enum DescentRoomCue { FogEntry, SplashOpen, Crack }

/// <summary>
/// THE SPIRAL ROOM'S THREE NOTES — the fog era's door, the splash's chime, and the crack of
/// the screen at zero (owner ruling 2026-08-16: "we should add SFX ... so we know its not stuck").
/// Shared across heads; each head supplies its own asset resolver (WPF: <c>DescentRoomSfx</c>).
///
/// <para><b>Why this exists rather than three bare one-shot calls.</b> Three reasons, and each one
/// is a bug that would otherwise be written three times. First, the subject's own kill switch:
/// <c>DescentCountdownAudio</c> already turns the heartbeat off without a patch, and a room that
/// kept chiming after the countdown had been silenced would make that switch a lie. Second, the
/// fallback ladder: each cue names a DEDICATED asset slot first and an existing on-theme sound
/// second, so the owner can drop a bespoke file in and it wins with no code change. Third, the
/// naming — three cue names spelled once, in the file that owns the feature, instead of three
/// magic strings scattered across a view and a control.</para>
///
/// <para><b>It ships PARTLY dark, on purpose.</b> None of the three dedicated assets
/// (<c>descent_fog_enter</c>, <c>descent_spiral_open</c>, <c>descent_crack</c>) exist yet —
/// those are an owner call, exactly like <c>Resources/descent/heartbeat.wav</c> is for the
/// heartbeat. What DOES ship is the second rung of each ladder: three sounds already in the box
/// that read right for the moment. So the room is audible today and gets its own voice the moment
/// somebody authors one, and if BOTH rungs are ever missing the cue is a silent no-op with one
/// Debug line for the life of the process — never a throw and never a placeholder tone.</para>
///
/// <para><b>Volume the way the rest of the UI does it.</b> Master volume is the app's mute (there
/// is no separate flag — 0 IS muted), re-read on every cue so a change made mid-session is
/// respected by the next one, and multiplied by a per-cue fraction. The playback itself goes
/// through <see cref="CoreAudio.PlayOneShot"/> (the head's one-shot player) like every other
/// one-shot in the app, which owns the device lifetime, the concurrency cap and the failure
/// circuit breaker — nothing here opens an audio device of its own.</para>
///
/// <para><b>Nothing here ducks.</b> Ducking lowers OTHER processes and skips this one, so a
/// one-shot inside the app needs no ducking call and has no ref-count to balance. The one ordering
/// that does matter is already handled elsewhere: the show director stops the heartbeat before
/// the zero show opens.</para>
/// </summary>
public static class DescentRoomAudio
{
    // ---- the ladders. Dedicated slot first, shipped stand-in second. ----

    /// <summary>Walking into the fog: a low deepening swell, not a chime. The room is being
    /// entered, not announced.</summary>
    private static readonly string[] FogEntry = { "descent_fog_enter", "ui_deepen" };

    /// <summary>The splash: one soft chime as the spiral starts opening. It fires ONCE per
    /// entry, never per retry — a chime that repeated while a browser struggled would be the
    /// sound of the thing being stuck, which is the exact impression the splash exists to
    /// remove.</summary>
    private static readonly string[] SplashOpen = { "descent_spiral_open", "reveal_chime" };

    /// <summary>The crack itself. Fired by the fuse window on the frame the first hairline
    /// splits the glyph — NOT by the chip (owner note 0816: the sting used to ride the chip's
    /// flash-out and landed 1.5s of freeze ahead of the visual; the chip is silent now and the
    /// sound belongs to the screen that cracks). Two dedicated slots so the file the owner was
    /// already told about keeps working, then the shipped stand-in.</summary>
    private static readonly string[] Crack = { "descent_crack", "descent_zero_flash", "freeze_trigger" };

    // ---- the mix ----

    /// <summary>Under the fog's own weather, not over it.</summary>
    private const float FogEntryScale = 0.42f;

    /// <summary>The quietest of the three: it plays while somebody is waiting, and a loud
    /// noise at that moment reads as an error tone.</summary>
    private const float SplashOpenScale = 0.34f;

    /// <summary>The loudest, and the only one that is allowed to be startling.</summary>
    private const float CrackScale = 0.55f;

    private static bool _missingLogged;

    /// <summary>
    /// The gate, as pure arithmetic so "the room is silent when the subject asked for silence"
    /// is testable without an audio device or a settings file. Same shape and same reasoning as
    /// the heartbeat's ShouldPlay, minus the asset term — the resolver answers that question per
    /// cue, because each cue has two candidates rather than one path.
    /// </summary>
    public static bool ShouldPlay(bool audioEnabled, int masterVolume) => audioEnabled && masterVolume > 0;

    /// <summary>
    /// Resolve the first candidate that is actually on disk and play it once. Every failure
    /// path — the switch off, the app muted, both assets absent, a resolver that threw — ends
    /// in silence rather than in an exception reaching a UI event handler.
    /// </summary>
    public static void Play(DescentRoomCue cue, bool audioEnabled, int masterVolume, Func<string, string> resolve)
    {
        if (!ShouldPlay(audioEnabled, masterVolume)) return;
        var (candidates, scale, tag) = cue switch
        {
            DescentRoomCue.FogEntry => (FogEntry, FogEntryScale, "descent-fog-enter"),
            DescentRoomCue.SplashOpen => (SplashOpen, SplashOpenScale, "descent-spiral-open"),
            DescentRoomCue.Crack => (Crack, CrackScale, "descent-crack"),
            _ => throw new ArgumentOutOfRangeException(nameof(cue)),
        };
        try
        {
            foreach (var name in candidates)
            {
                var path = resolve(name);
                if (string.IsNullOrEmpty(path)) continue;
                CoreAudio.PlayOneShot(path, Math.Clamp(masterVolume / 100f * scale, 0f, 1f), tag);
                return;
            }
            if (_missingLogged) return;

            // ONE line for the life of the process. A missing asset is a silent no-op by
            // contract, and log spam from a cue that fires on every tab entry would bury the
            // lines that matter.
            _missingLogged = true;
            Log.Debug("[Spiral] No sound for {Tag} ({Candidates}) - the room stays quiet.", tag, string.Join(", ", candidates));
        }
        catch (Exception ex) { Log.Debug("[Spiral] sfx {Tag} failed: {E}", tag, ex.Message); }
    }
}
