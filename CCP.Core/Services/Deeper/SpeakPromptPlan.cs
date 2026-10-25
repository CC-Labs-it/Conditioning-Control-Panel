using System;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>What the hold watch does on one tick near the region end.</summary>
    public enum SpeakHoldStep { None, SeekToRegionStart, Pause }

    /// <summary>How one listen result counts toward the prompt.</summary>
    public enum SpeakHeard
    {
        /// <summary>Mic briefly held elsewhere: retry without feedback.</summary>
        Retry,
        /// <summary>A rep: flash the correct message.</summary>
        Correct,
        /// <summary>Pure silence: keep listening without nagging.</summary>
        Silence,
        /// <summary>Heard something else: flash the incorrect message.</summary>
        Incorrect
    }

    /// <summary>
    /// The rules of one <c>speak</c> Deeper effect, decided once from the authored action:
    /// cue text and cadence, reps, feedback lines, the wall-clock deadline and when playback is
    /// held. The head's session owns the cue UI, the recognizer and the timers.
    /// </summary>
    public sealed class SpeakPromptPlan
    {
        // Loop/pause this far (seconds) before the exact region end so the engine doesn't
        // flag a band exit on the same tick we try to hold.
        public const double HoldLookaheadSec = 0.30;

        public string Target { get; }
        public string Cue { get; }
        public SpeakCueMode CueMode { get; }
        public int CueIntervalMs { get; }
        public int RequiredReps { get; }
        public SpeakCompletion Completion { get; }
        public SpeakHoldMode HoldMode { get; }
        public string CorrectMessage { get; }
        public string IncorrectMessage { get; }
        public double? RegionStart { get; }
        public double? RegionEnd { get; }

        /// <summary>When set, the prompt stops listening at this UTC time (Duration completion, or
        /// a one-shot rule fire with no region to hold). Null: listen until satisfied/stopped.</summary>
        public DateTime? HardDeadline { get; }

        public SpeakPromptPlan(TriggerEffectAction effect, DateTime nowUtc)
        {
            Target = (effect.SpeakTarget ?? "").Trim();
            var cue = (effect.SpeakCue ?? "").Trim();
            Cue = string.IsNullOrWhiteSpace(cue)
                ? (string.IsNullOrWhiteSpace(Target) ? "Say it" : $"Say {Target}")
                : cue;
            CueMode = effect.SpeakCueMode;
            CueIntervalMs = Math.Max(80, effect.SpeakCueIntervalMs);
            RequiredReps = Math.Clamp(effect.SpeakRequiredReps, 1, 5);
            Completion = effect.SpeakCompletion;
            HoldMode = effect.SpeakHoldMode;
            CorrectMessage = string.IsNullOrWhiteSpace(effect.SpeakCorrectMessage) ? $"that's it, {VocabTokens.PetName}" : effect.SpeakCorrectMessage!.Trim();
            IncorrectMessage = string.IsNullOrWhiteSpace(effect.SpeakIncorrectMessage) ? "try again" : effect.SpeakIncorrectMessage!.Trim();
            RegionStart = effect.SpeakRegionStartSec;
            RegionEnd = effect.SpeakRegionEndSec;

            // Duration completion (or a non-band one-shot with bounds) caps the prompt at the
            // band's wall-clock width; UntilSatisfied with a region runs open-ended (held).
            if (Completion == SpeakCompletion.Duration && RegionStart.HasValue && RegionEnd.HasValue)
            {
                var widthMs = Math.Max(500, (RegionEnd.Value - RegionStart.Value) * 1000.0);
                HardDeadline = nowUtc.AddMilliseconds(widthMs);
            }
            else if (!HasRegion)
            {
                // One-shot rule fire with no band: give it a bounded default so it can't listen forever.
                HardDeadline = nowUtc.AddSeconds(30);
            }
        }

        public bool HasRegion => RegionStart.HasValue && RegionEnd.HasValue;

        /// <summary>Only UntilSatisfied with a non-passive hold mode holds playback at all; Duration
        /// mode just lets the band end normally.</summary>
        public bool HoldsPlayback => Completion == SpeakCompletion.UntilSatisfied && HoldMode != SpeakHoldMode.KeepPlaying;

        /// <summary>
        /// #1221: a rule-fired speak has no authored width, so the engine hands it a null region
        /// end. There is no region end to police then: the hold IS the effect, so a Pause hold is
        /// taken at cue time. LoopRegion genuinely needs bounds (nowhere to seek back to) so it
        /// stays a no-op. The 30s hard deadline still bounds the pause.
        /// </summary>
        public bool PausesAtCue => HoldsPlayback && !HasRegion && HoldMode == SpeakHoldMode.Pause;

        /// <summary>The hold watch's decision at playhead <paramref name="t"/>.</summary>
        public SpeakHoldStep HoldStepAt(double t, bool paused)
        {
            if (!HoldsPlayback || !HasRegion) return SpeakHoldStep.None;
            if (t < RegionEnd!.Value - HoldLookaheadSec) return SpeakHoldStep.None;
            if (HoldMode == SpeakHoldMode.LoopRegion) return SpeakHoldStep.SeekToRegionStart;
            if (HoldMode == SpeakHoldMode.Pause && !paused) return SpeakHoldStep.Pause;
            return SpeakHoldStep.None;
        }

        public static SpeakHeard Classify(PhraseResult res)
        {
            if (res.Unavailable) return SpeakHeard.Retry;
            if (res.Matched) return SpeakHeard.Correct;
            if (res.TimedOut && string.IsNullOrWhiteSpace(res.Transcript)) return SpeakHeard.Silence;
            return SpeakHeard.Incorrect;
        }
    }
}
