using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// What firing an <see cref="EffectPayload"/> DOES on this head. The payloads moved to Core as
/// data; these are their old <c>Fire()</c> bodies, unchanged, reached through
/// <see cref="EffectPayload.Firer"/> (seeded in App startup).
/// </summary>
internal static class EffectPayloadFiring
{
    internal static void Fire(EffectPayload payload)
    {
        switch (payload)
        {
            case FlashPayload p: Flash(p); break;
            case SubliminalPayload: Subliminal(); break;
            case OverlayPayload p: Overlay(p); break;
            case BrainDrainMeltPayload: BrainDrainMelt(); break;
            case VideoPayload p: Video(p); break;
            case HtLinkPayload: HtLink(); break;
            case AudioPayload: Audio(); break;
            case BambiFreezePayload: BambiFreeze(); break;
            case BouncingTextPayload: BouncingText(); break;
            case GifCascadePayload: GifCascade(); break;
        }
    }

    private static void Flash(FlashPayload p)
    {
        try
        {
            var (amount, duration, size) = p.ResolveBurst(App.Settings?.Current);
            if (p.ImagePath != null && p.OriginPx is { } origin)
                App.Flash?.TriggerFlashOnceWithImage(p.ImagePath, duration, playSound: true,
                    bubbleOrigin: new Point(origin.X, origin.Y), bubbleDiameter: p.FaceDiameterPx, size: size, amount: amount);
            else
                App.Flash?.TriggerFlashOnce(amount, duration, size, suppressHaptic: false);
        }
        catch (Exception ex) { App.Logger?.Debug("FlashPayload: {E}", ex.Message); }
    }

    private static void Subliminal()
    {
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("SubliminalPayload: {E}", ex.Message); }
    }

    private static void Overlay(OverlayPayload p)
    {
        try
        {
            int duration = (int)(p.Scale(1500, 4500) * EffectPayload.GlobalDurationMult * p.DurationMult);
            if (p.OverlayKind == "braindrain")
            {
                // braindrain = a full-screen wash of a random flash-pool image/gif.
                if (p.BraindrainOpacity is double o) ChaosFlashOverlay.Show(duration, o);  // glitch: ~30%
                else ChaosFlashOverlay.Show();                                              // chaos: faint ~10%/10s default
            }
            else
            {
                double opacity = p.ScaleD(0.25, 0.70);   // pink_filter / spiral take a plain 0..1 opacity
                App.Overlay?.ShowOverlayTimed(p.OverlayKind, duration, opacity);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("OverlayPayload: {E}", ex.Message); }
    }

    /// <summary>Every decision lives in <see cref="BrainDrainBubble"/>; this is only the wiring.</summary>
    private static void BrainDrainMelt()
    {
        try
        {
            var overlay = App.Overlay;
            if (overlay == null) return;

            // One drain at a time. The user's own loop wins outright - it has no duration, so a
            // timed overlay landing on top of it would end by tearing THEIR drain down ten seconds
            // later. The pop still paid its XP before we got here, so nothing is lost.
            bool userDrainUp = App.BrainDrain?.IsRunning == true || overlay.BrainDrainVisualUp;
            if (!BrainDrainBubble.ShouldPlayOverlay(overlay.TimedBrainDrainActive, userDrainUp))
            {
                App.Logger?.Information("Bubble: brain drain pop paid XP only - a drain is already up");
                return;
            }

            // The pop borrows the user's own blur dial, so a dial at 0 means this bubble has no
            // picture to show either. XP is already paid; the bubble is its own reward.
            var strength = App.Settings?.Current?.BrainDrainBlurStrength ?? 50;
            if (BrainDrainVisualPolicy.IsSilent(strength))
            {
                App.Logger?.Information("Bubble: brain drain pop paid XP only - the blur dial is at 0");
                return;
            }

            var kind = BrainDrainBubble.OverlayKindFor(MotionFx.Level);
            overlay.ShowOverlayTimed(kind, BrainDrainBubble.OverlayMs,
                                     BrainDrainBubble.OverlayOpacity(strength));
        }
        catch (Exception ex) { App.Logger?.Debug("BrainDrainMeltPayload: {E}", ex.Message); }
    }

    private static void Video(VideoPayload p)
    {
        try
        {
            // Only chaos caps playback at ~15s, making a random start a "random slice".
            // Dashboard trigger bubbles reuse this payload uncapped, so arming there made
            // the full mandatory video start midway (#456/#458).
            if (!p.Ambient)
                App.Video?.ArmRandomSegment(VideoPayload.SEGMENT_SEC);

            if (App.Video == null)
            {
                // Was a silent null-conditional: the pop simply evaporated (#1135).
                App.Logger?.Information("VideoPayload: nothing to fire - the video service is not available");
                return;
            }

            // userEarned: a human popped this bubble on purpose. It is the one thing that separates
            // this call from the background scheduler, and the guards inside TriggerVideo that exist
            // to keep the SCHEDULER out of the user's way read it so they no longer eat a video the
            // user asked for (#1135).
            App.Video.TriggerVideo(silentIfEmpty: true, userEarned: true, chaosToken: p.ChaosToken);
        }
        catch (Exception ex)
        {
            // Information, not Debug: a Debug line is below the default log level, so this was the
            // second place a popped video bubble could fail leaving nothing behind at all (#1135).
            App.Logger?.Information(ex, "VideoPayload: firing the video failed - the pop produced nothing");
        }
    }

    private static void HtLink()
    {
        try
        {
            // Never navigate over a video that's already playing — this payload used to fire
            // blind, which is the "AI effects just stop your video halfway through" report.
            if (App.BrowserMedia?.ShouldDeferNewVideo == true)
            {
                App.Logger?.Debug("HtLinkPayload: browser media active (or in cool-off) - skipping");
                return;
            }

            var url = HtLinkPool.PickRandom();
            if (string.IsNullOrWhiteSpace(url)) return;
            if (Application.Current?.MainWindow is MainWindow mw)
            {
                // Claim the slot before navigating so nothing else stacks on the clip we open.
                if (App.BrowserMedia?.BeginTakeover(
                        Services.Browser.BrowserMediaService.MediaOwner.Chaos) == false)
                    return;
                // userInitiated: false - a chaos payload fires on its own schedule, so an offline
                // block here has no click to explain and must not toast.
                mw.NavigateToUrlInBrowser(url!, autoPlayFullscreen: true, userInitiated: false);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("HtLinkPayload: {E}", ex.Message); }
    }

    private static void Audio()
    {
        // No dedicated SFX pool yet — reuse the subliminal whisper for an audio sting.
        try { App.Subliminal?.FlashSubliminal(); }
        catch (Exception ex) { App.Logger?.Debug("AudioPayload: {E}", ex.Message); }
    }

    private static void BambiFreeze()
    {
        try { App.Subliminal?.TriggerBambiFreeze(); }
        catch (Exception ex) { App.Logger?.Debug("BambiFreezePayload: {E}", ex.Message); }
    }

    /// <summary>REUSES App.BouncingText; no-op if the bouncing toy is already running for the user.</summary>
    private static void BouncingText()
    {
        try
        {
            var svc = App.BouncingText;
            if (svc == null || svc.IsRunning) return;   // don't hijack a user-started bouncer

            // Source a single affirmation from the existing enabled phrase pool.
            string? phrase = PickAffirmation();
            var pool = phrase != null ? new System.Collections.Generic.List<string> { phrase } : null;
            svc.Start(bypassLevelCheck: true, pool: pool);

            int bounces = 0;
            EventHandler? onBounce = null;
            var life = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(Math.Max(0.5, BouncingTextPayload.DURATION_SEC * EffectPayload.GlobalDurationMult)) };

            void StopOnce()
            {
                try { life.Stop(); } catch { }
                if (onBounce != null) { try { svc.OnBounce -= onBounce; } catch { } }
                try { svc.Stop(); } catch { }
            }

            onBounce = (_, _) => { if (++bounces >= BouncingTextPayload.MAX_BOUNCES) StopOnce(); };
            svc.OnBounce += onBounce;
            life.Tick += (_, _) => StopOnce();
            life.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("BouncingTextPayload: {E}", ex.Message); }
    }

    private static string? PickAffirmation()
    {
        try
        {
            var pool = App.Settings?.Current?.BouncingTextPool;
            if (pool == null) return null;
            var enabled = new System.Collections.Generic.List<string>();
            foreach (var kv in pool) if (kv.Value) enabled.Add(kv.Key);
            if (enabled.Count == 0) return null;
            return enabled[_rng.Next(enabled.Count)];
        }
        catch { return null; }
    }

    private static readonly Random _rng = new();

    private static void GifCascade()
    {
        try
        {
            ChaosGifCascadeOverlay.Show(
                spawnRatePerSec: GifCascadePayload.SPAWN_RATE_PER_SEC,
                durationSec: GifCascadePayload.DURATION_SEC * EffectPayload.GlobalDurationMult,
                gifSize: GifCascadePayload.GIF_SIZE,
                fallSpeed: GifCascadePayload.FALL_SPEED,
                opacity: GifCascadePayload.OPACITY,
                startScale: GifCascadePayload.START_SCALE);
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascadePayload: {E}", ex.Message); }
    }
}
