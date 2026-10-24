using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Engine-off audio diagnostic from WPF #1414. Call only when playback actually starts.
    /// Serilog receives one filename-only line per player per ten seconds; failures never stop audio.
    /// </summary>
    public static class CoreEngineOffAudioLog
    {
        /// <summary>Minimum gap between two lines for the same player.</summary>
        public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(10);

        private static readonly Dictionary<string, DateTime> _lastLine = new(StringComparer.Ordinal);
        private static readonly object _gate = new();

        /// <summary>The engine is started, or a session is running.</summary>
        public static bool EngineRunning => IsRunning(CoreSession.IsEngineRunning, CoreSession.IsSessionRunning);

        /// <summary>Pure half of <see cref="EngineRunning"/>.</summary>
        public static bool IsRunning(bool engineRunning, bool sessionRunning) => engineRunning || sessionRunning;

        /// <summary>
        /// A sound from <paramref name="player"/> just started. Logs it when the engine is off.
        /// <paramref name="asked"/> = the player pressed a button for exactly this sound (a Test or
        /// Preview button): never logged, it is not a mystery.
        /// </summary>
        public static void Note(string player, string? path, bool asked = false)
        {
            try
            {
                var line = LineFor(player, path, EngineRunning, asked, DateTime.UtcNow);
                if (line != null) Log.Information("[VideoDiag AUDIO] {Line}", line);
            }
            catch
            {
                // A diagnostic must never cost a sound.
            }
        }

        /// <summary>
        /// The line to write, or null for "write nothing" (engine running, an asked-for sound, or
        /// this player already wrote one inside <see cref="MinGap"/>).
        /// </summary>
        public static string? LineFor(string player, string? path, bool engineRunning, bool asked, DateTime nowUtc)
        {
            if (engineRunning || asked) return null;
            if (string.IsNullOrEmpty(player)) player = "audio";

            lock (_gate)
            {
                if (_lastLine.TryGetValue(player, out var last) && nowUtc - last < MinGap && nowUtc >= last)
                    return null;
                _lastLine[player] = nowUtc;
            }

            return $"engine off: {player} started '{NameOf(path)}'";
        }

        /// <summary>File name only: paths may contain private profile information.</summary>
        public static string NameOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "?";
            try
            {
                var name = Path.GetFileName(path.Replace('\\', '/').TrimEnd('/'));
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }

    }
}
