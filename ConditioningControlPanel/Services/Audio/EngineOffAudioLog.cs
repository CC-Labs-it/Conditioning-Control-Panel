using System;

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
    public static partial class EngineOffAudioLog
    {
        /// <summary>The engine is started, or a session is running.</summary>
        public static bool EngineRunning => IsRunning(App.IsEngineRunning, App.IsSessionRunning);

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
                var line = CoreAudioDiagnostics.HeldLineFor(player, what, DateTime.UtcNow);
                if (line != null) VideoDiag.Log("AUDIO", line);
            }
            catch
            {
            }
        }
    }
}
