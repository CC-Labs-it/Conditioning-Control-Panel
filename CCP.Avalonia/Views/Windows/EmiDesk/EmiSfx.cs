using System;
using System.IO;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// Her small sounds: the WPF head's Services/EmiDesk/EmiSfx.cs pat and ring cues, same chains,
    /// scales and 130 ms per-cue throttle, played through Core <see cref="CoreAudio"/>. The first
    /// candidate that exists under Resources/sounds wins; none is silence.
    ///
    /// <para>SAFETY IS SILENCE (WPF EmiSfx.Audible): Core <see cref="CoreAudio.IsOutputSuppressed"/>, a
    /// master volume of 0 and an active line-engine hold each mute her. The throttle slot is spent
    /// before that gate, as on WPF, so a muted cue still counts against the 130 ms floor.
    /// Cues resolve through CoreModArt's audio override (.wav/.mp3 swap) before ContentLocator,
    /// matching WPF ModResourceResolver.ResolveAudioPath for each candidate in order.
    /// The toss cues (Lift/Bump/Thud/Chime) come with the toss port (row win-emi-desk-extras).</para>
    /// </summary>
    internal static class EmiSfx
    {
        private const float PatScale = 0.12f;
        private const float OpenScale = 0.17f;
        private const float CloseScale = 0.13f;
        private const int MinGapMs = 130;

        /// <summary>The throttle's clock; tests step it.</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        private static readonly object Gate = new();
        private static DateTimeOffset _lastPat = DateTimeOffset.MinValue;
        private static DateTimeOffset _lastRing = DateTimeOffset.MinValue;

        public static void Pat()
        {
            if (Throttle(ref _lastPat))
                Play(new[] { "emi/pat.mp3", "chaos/chip_pop.mp3", "bubbles/Pop3.mp3" }, PatScale, "emi-sfx-pat");
        }

        public static void RingOpen()
        {
            if (Throttle(ref _lastRing))
                Play(new[] { "emi/ring_open.mp3", "chaos/cards_in.mp3", "chaos/reveal_chime.mp3" }, OpenScale, "emi-sfx-ring");
        }

        public static void RingClose()
        {
            if (Throttle(ref _lastRing))
                Play(new[] { "emi/ring_close.mp3", "chaos/ui_unequip.mp3", "chaos/sink.mp3" }, CloseScale, "emi-sfx-ring");
        }

        private static bool Throttle(ref DateTimeOffset slot)
        {
            var now = Clock.GetUtcNow();
            lock (Gate)
            {
                // Abs: a clock stepped backwards (NTP, a test clock) must not mute her until it catches up.
                if (Math.Abs((now - slot).TotalMilliseconds) < MinGapMs) return false;
                slot = now;
                return true;
            }
        }

        /// <summary>WPF EmiSfx.Audible: endpoint suppressed, master volume 0, or a hold => silent.</summary>
        private static bool Audible(out float master)
        {
            master = 0f;
            if (CoreAudio.IsOutputSuppressed) return false;
            int level = CoreSettings.Current?.MasterVolume ?? 0;
            if (level <= 0) return false;
            if (EmiDeskService.Instance.LineEngine.HoldActive) return false;
            master = Math.Clamp(level / 100f, 0f, 1f);
            return master > 0f;
        }

        private static void Play(string[] candidates, float scale, string tag)
        {
            try
            {
                if (!Audible(out float master)) return;
                foreach (var rel in candidates)
                {
                    var path = CoreModArt.AudioOverridePath(rel)
                        ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    CoreAudio.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] sfx {Tag} failed", tag);
            }
        }
    }
}
