using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// ccp-bugs #1414 ("something is playing random audio and I can't figure out what", engine
    /// never started). Two small things live here:
    ///
    /// <para>THE GATE: <see cref="EngineRunning"/> is the one answer to "is the engine (or a
    /// session) running", used by the Awareness keyword triggers to keep their AUDIO quiet while
    /// nothing is running.</para>
    ///
    /// <para>THE LOG: every app-owned player calls <see cref="Note"/> when a sound actually starts.
    /// With the engine off it writes one line naming the player and the file NAME (never the path)
    /// into the VideoDiag trace, which is the log a bug report uploads. At most one line per player
    /// per ten seconds. It never blocks, never throws and never changes what plays.</para>
    /// </summary>
    public static class EngineOffAudioLog
    {
        /// <summary>Minimum gap between two lines for the same player.</summary>
        public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(10);

        private static readonly Dictionary<string, DateTime> _lastLine = new(StringComparer.Ordinal);
        private static readonly object _gate = new();

        /// <summary>The engine is started, or a session is running.</summary>
        public static bool EngineRunning => IsRunning(App.IsEngineRunning, App.IsSessionRunning);

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
                if (line != null) VideoDiag.Log("AUDIO", line);
            }
            catch
            {
                // A diagnostic must never cost a sound.
            }
        }

        /// <summary>
        /// <paramref name="player"/> kept a sound quiet because the engine is off (the keyword
        /// trigger guard). Same sink, same rate limit, its own slot.
        /// </summary>
        public static void NoteHeld(string player, string what)
        {
            try
            {
                if (LineFor(player + ":held", null, engineRunning: false, asked: false, DateTime.UtcNow) != null)
                    VideoDiag.Log("AUDIO", $"engine off: {player} held its {what}");
            }
            catch
            {
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

        /// <summary>File name only: a full path can carry the Windows user name.</summary>
        public static string NameOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "?";
            try
            {
                var name = Path.GetFileName(path.TrimEnd('/', '\\'));
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>Test seam: forget every player's last line.</summary>
        internal static void ResetForTests()
        {
            lock (_gate) _lastLine.Clear();
        }
    }
}
