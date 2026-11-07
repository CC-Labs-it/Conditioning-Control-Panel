using System;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The kinds of one-shot effect a Chaos bubble can carry. Each maps to a
/// concrete <see cref="EffectPayload"/> that a head fires through an existing one-shot trigger.
/// </summary>
public enum EffectBubblePayloadKind
{
    Flash,
    Subliminal,
    Overlay,
    Video,
    HtLink,
    Audio,
    BambiFreeze,
    BouncingText,
    GifCascade
}

/// <summary>
/// A Chaos bubble's payload — the effect that fires when the bubble is popped
/// (benign "treat") or when a live bubble's fuse expires undefused
/// ("detonation"). Concrete payloads are thin wrappers over the app's existing
/// one-shot triggers (FlashService, SubliminalService, …). <see cref="Strength"/>
/// (0..100, derived from bubble size at spawn) scales the effect: bigger bubble
/// ⇒ stronger payload.
///
/// <see cref="Fire"/> is invoked on the UI thread (from a timer tick or a click handler).
/// The payloads are data here; what firing DOES is head-side, through <see cref="Firer"/>,
/// which each head seeds at startup (WPF: EffectPayloadFiring; Avalonia: Gif Rain dispatch,
/// with logging for unported payloads). The head's services own their own dispatching/threading.
/// </summary>
public abstract class EffectPayload
{
    /// <summary>Short human label for the bubble tag, payload feed and bark context.</summary>
    public abstract string DisplayName { get; }

    public abstract EffectBubblePayloadKind Kind { get; }

    /// <summary>0..100, scaled from bubble size at spawn. Drives effect intensity.</summary>
    public int Strength { get; set; }

    /// <summary>Per-instance multiplier on this payload's time-based duration(s). 1.0 = nominal.
    /// Dashboard trigger bubbles raise it so effects linger longer than the brisk chaos cadence.</summary>
    public double DurationMult { get; set; } = 1.0;

    /// <summary>True when this payload was built for a dashboard "Trigger Bubble" rather than a
    /// chaos-run bubble (set in ChaosBubbleVariants.Build). Some payloads behave differently on
    /// the calm dashboard — e.g. video must not arm a random start segment there (#456/#458).
    /// A per-instance flag, NOT App.Chaos.IsRunning at fire time: ambient bubbles can pop while
    /// a chaos run happens to be active, and a run can end between spawn and pop.</summary>
    public bool Ambient { get; set; }

    /// <summary>
    /// Global multiplier on time-based payload durations (flashes/overlays linger). Set &gt;1 by
    /// the darter slow-mo power-up so "images last longer" while time is slowed; restored to 1
    /// when it ends. 1.0 = normal.
    /// </summary>
    public static double GlobalDurationMult { get; set; } = 1.0;

    /// <summary>The head's effect runner. Unseeded = firing does nothing (a head with no effects yet).</summary>
    public static Action<EffectPayload>? Firer { get; set; }

    /// <summary>Run the wrapped one-shot effect, scaled by <see cref="Strength"/>.</summary>
    public void Fire()
    {
        try { Firer?.Invoke(this); }
        catch (Exception ex) { Log.Debug("{Payload}: {E}", GetType().Name, ex.Message); }
    }

    /// <summary>Lerp helper: map Strength 0..100 onto [min,max].</summary>
    internal int Scale(int min, int max) =>
        min + (int)Math.Round((max - min) * Math.Clamp(Strength, 0, 100) / 100.0);

    internal double ScaleD(double min, double max) =>
        min + (max - min) * Math.Clamp(Strength, 0, 100) / 100.0;
}

/// <summary>Fires a flash burst — more/bigger/faster images at higher Strength.</summary>
public sealed class FlashPayload : EffectPayload
{
    internal bool UseFlashSettings { get; set; }
    internal string? ImagePath { get; set; }
    internal (double X, double Y)? OriginPx { get; set; }
    internal double FaceDiameterPx { get; set; }
    public override string DisplayName => "flash";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Flash;

    /// <summary>
    /// Ambient (dashboard trigger-bubble) pop-flash duration in ms (#593): the user's global Flash
    /// Duration (seconds, clamped 1..30) instead of the chaos Strength-scaled ~3-4s burst. Pure so it
    /// can be unit-tested without App.Settings.
    /// </summary>
    internal static int AmbientFlashDurationMs(int flashDurationSeconds)
        => Math.Clamp(flashDurationSeconds, 1, 30) * 1000;

    internal (int Amount, int DurationMs, int Size) ResolveBurst(Models.AppSettings? settings)
    {
        if (settings != null && (Ambient || UseFlashSettings))
            return (settings.SimultaneousImages, AmbientFlashDurationMs(settings.FlashDuration), settings.ImageScale);
        return (Scale(1, 3), (int)(Scale(900, 2000) * GlobalDurationMult * DurationMult), Scale(68, 143));
    }

}

/// <summary>Flashes a subliminal from the active pool.</summary>
public sealed class SubliminalPayload : EffectPayload
{
    public override string DisplayName => "subliminal";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Subliminal;
}

/// <summary>Snaps an overlay (pink_filter / spiral / braindrain) on for a few seconds.</summary>
public sealed class OverlayPayload : EffectPayload
{
    internal string OverlayKind { get; }
    internal double? BraindrainOpacity { get; }
    // braindrainOpacity: when set, the braindrain wash uses this opacity (0..1) for the
    // computed duration instead of its faint ~10%/10s default. Used by the dashboard "glitch"
    // trigger bubble (~30%); null keeps chaos's original faint wash untouched.
    public OverlayPayload(string overlayKind, double? braindrainOpacity = null)
    {
        OverlayKind = overlayKind;
        BraindrainOpacity = braindrainOpacity;
    }

    public override string DisplayName => OverlayKind;
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Overlay;
}

/// <summary>
/// Bubbles v2, wave 2: the Brain Drain bubble's pop. Ten seconds of the real Brain Drain
/// overlay - the melting-glass one at full motion, the plain blur under Reduced/Off - at the
/// user's own blur strength. Deliberately NOT <see cref="OverlayPayload"/>'s "braindrain" arm,
/// which is a full-screen wash of a flash image and never touches the drain at all.
///
/// Every decision here lives in <see cref="BrainDrainBubble"/>; this is only the wiring.
/// </summary>
public sealed class BrainDrainMeltPayload : EffectPayload
{
    public override string DisplayName => "braindrain melt";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Overlay;
}

/// <summary>Triggers a mandatory video (silent no-op if the video pool is empty).</summary>
public sealed class VideoPayload : EffectPayload
{
    public override string DisplayName => "video";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Video;

    /// <summary>Seconds of video the chaos tape shows — a random slice, ended by the chaos hard cap.</summary>
    public const double SEGMENT_SEC = 15;

    /// <summary>
    /// Nonzero while a descent run owns this request: the token VideoService hands back from
    /// ClaimChaosVideoToken, taken by ChaosModeService at detonation so the run can call the video
    /// back if it ends before the tape reaches the screen (ccp-bugs #1201). Zero for a dashboard
    /// trigger bubble, which no run owns and nothing may cancel.
    /// </summary>
    public int ChaosToken { get; set; }

}

/// <summary>Opens a HypnoTube link from the pool in the embedded browser, auto-fullscreen.</summary>
public sealed class HtLinkPayload : EffectPayload
{
    public override string DisplayName => "HT link";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.HtLink;
}

/// <summary>Plays a one-shot subliminal whisper (reuses the subliminal audio path).</summary>
public sealed class AudioPayload : EffectPayload
{
    public override string DisplayName => "whisper";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.Audio;
}

/// <summary>Triggers a Bambi Freeze (special subliminal + audio).</summary>
public sealed class BambiFreezePayload : EffectPayload
{
    public override string DisplayName => "freeze";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BambiFreeze;
}

/// <summary>
/// A single short affirmation bouncing DVD-logo style across the screen, click-through, for a short
/// window. REUSES the existing <see cref="BouncingTextService"/> (App.BouncingText) rather than building
/// a new bouncer — sourcing its text from the existing affirmation/phrase pool — and tears it down after
/// <see cref="DURATION_SEC"/> or <see cref="MAX_BOUNCES"/>, whichever comes first. Conservative defaults.
/// No-op if the bouncing toy is already running for the user (so a payload never hijacks it).
/// </summary>
public sealed class BouncingTextPayload : EffectPayload
{
    public override string DisplayName => "bouncing text";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.BouncingText;

    // ---- named tunables (conservative defaults) ----
    public const double DURATION_SEC = 8.0;   // on-screen lifetime
    public const int    MAX_BOUNCES  = 12;    // auto-stop after this many bounces (whichever first)
    public const double OPACITY      = 0.85;  // text opacity (informational; service reads its own setting)
    public const int    TEXT_SIZE    = 120;   // % of base font (informational tunable)
    public const int    SPEED        = 5;     // 1..10 bounce speed (informational tunable)
}

/// <summary>
/// Images/gifs spawn at the top of the screen and fall/cascade downward, click-through, for a short
/// window. Sources its images from the SAME flash/braindrain image pool (EffectiveAssetsPath/images,
/// via <see cref="ChaosGifCascadeOverlay"/>). Conservative named defaults.
/// </summary>
public sealed class GifCascadePayload : EffectPayload
{
    public override string DisplayName => "gif cascade";
    public override EffectBubblePayloadKind Kind => EffectBubblePayloadKind.GifCascade;

    // ---- named tunables (the "to start with" defaults — boons will tune these later) ----
    public const double SPAWN_RATE_PER_SEC = 1.67;  // ~10 clips across the 6s window (2026-06-10: denser, shorter)
    public const double DURATION_SEC       = 6.0;   // spawn window
    public const double GIF_SIZE           = 400;   // px (max dimension)
    public const double START_SCALE        = 0.45;  // clips spawn small at the top and grow as they slide down
    public const double FALL_SPEED         = 3.6;   // DIPs per ~16ms frame — a quick, lively slide
    public const double OPACITY            = 0.9;   // per-image opacity

}
