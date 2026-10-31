using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The audio seam: the four things ported views ask of the audio service. The service itself
    /// is NAudio and LibVLC on Windows and stays in the head; a Linux backend is a later layer.
    ///
    /// <para>Unseeded means "no audio on this head": <see cref="PlayOneShot"/> fires the finished
    /// callback at once and returns, which is exactly what the service does when disposed, so a
    /// view that sequences on the sound still proceeds; ducking is a no-op and the generation
    /// never advances.</para>
    ///
    /// <para><c>ApplyPreferredDevice</c> is deliberately not here: it takes an NAudio or LibVLC
    /// player object, which is the head's business. A view that needs it keeps its note.</para>
    /// </summary>
    public static class CoreAudio
    {
        /// <summary>WPF AudioService's cadence for catching sessions born during a duck window.</summary>
        public const int DuckRescanIntervalMs = 2_500;

        /// <summary>Do not recapture an already-silent session as its original volume on a rescan.</summary>
        public static bool IsSilentForDuckRescan(float volume) => volume <= 0.001f;

        public static volatile Action<string, float, string, Action<TimeSpan>?, Action?>? PlayOneShotProvider;
        public static volatile Action<int>? DuckProvider;
        public static volatile Action<long>? UnduckProvider;
        public static volatile Func<long>? DuckGenerationProvider;

        public static void PlayOneShot(string path, float volume, string tag = "audio",
                                       Action<TimeSpan>? onStarted = null, Action? onFinished = null)
        {
            var p = PlayOneShotProvider;
            if (p is null) { try { onFinished?.Invoke(); } catch { } return; }
            try { p(path, volume, tag, onStarted, onFinished); }
            catch { try { onFinished?.Invoke(); } catch { } }
        }

        /// <summary>Optional: a player that also hands back the clip's stop (WPF's one-shot handle.Stop).</summary>
        public static volatile Func<string, float, string, Action<TimeSpan>?, Action?, Action?>? PlayStoppableProvider;

        /// <summary><see cref="PlayOneShot"/> that returns a stop for the clip (never null). Without a
        /// stoppable provider the clip plays through and the stop is a no-op.</summary>
        public static Action PlayStoppable(string path, float volume, string tag = "audio",
                                           Action<TimeSpan>? onStarted = null, Action? onFinished = null)
        {
            var p = PlayStoppableProvider;
            if (p is null) { PlayOneShot(path, volume, tag, onStarted, onFinished); return Noop; }
            try { return p(path, volume, tag, onStarted, onFinished) ?? Noop; }
            catch { try { onFinished?.Invoke(); } catch { } return Noop; }
        }

        public static volatile Action? StopSoundProvider;
        public static void StopSound() { try { StopSoundProvider?.Invoke(); } catch { } }
        public static volatile Action<string, int, Action<TimeSpan>?, Action?>? PlaySoundProvider;
        public static void PlaySound(string path, int volume, Action<TimeSpan>? started = null, Action? finished = null)
        {
            var play = PlaySoundProvider;
            if (play == null) { finished?.Invoke(); return; }
            try { play(path, volume, started, finished); } catch { finished?.Invoke(); }
        }

        private static void Noop() { }

        public static void Duck(int strength = 80) { try { DuckProvider?.Invoke(strength); } catch { } }
        public static void Unduck(long generation = -1) { try { UnduckProvider?.Invoke(generation); } catch { } }
        public static long DuckGeneration { get { try { return DuckGenerationProvider?.Invoke() ?? 0; } catch { return 0; } } }

        /// <summary>Optional: the head's "endpoint down, stay quiet" probe (WPF AudioService.IsOutputSuppressed).
        /// Unseeded reads false.</summary>
        public static volatile Func<bool>? IsOutputSuppressedProvider;
        public static bool IsOutputSuppressed { get { try { return IsOutputSuppressedProvider?.Invoke() ?? false; } catch { return false; } } }
    }
}
