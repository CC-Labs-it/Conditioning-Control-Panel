using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Where the recorded voice for a spoken phrase comes from, in one place. The subliminal player
    /// and the keyword trigger player both resolve through <see cref="Resolve"/>, and the phrase
    /// editors ask the same players whether a line has a voice, so the "stays silent" mark can never
    /// disagree with what plays (hard rule: no synthetic speech, a phrase with no clip is silent).
    ///
    /// <para>Order: the active mod's own clip wins. After that a mod that may borrow the bundled
    /// <c>Resources/sub_audio</c> clips (<see cref="ModAudioPolicy.UsesSharedSubAudio"/>) looks there,
    /// and every other mod (CCP Default, Locked) looks only at the recorded neutral words.</para>
    /// </summary>
    public static class SpokenClipRule
    {
        public static string? Resolve(string? modId, Func<string?> modClip, Func<string?> sharedClip, Func<string?> neutralClip)
        {
            var own = modClip();
            if (own != null) return own;
            return ModAudioPolicy.UsesSharedSubAudio(modId) ? sharedClip() : neutralClip();
        }

        /// <summary>True when the line shows but has no recorded voice. A blank line is not marked.</summary>
        public static bool IsSilent(string? phrase, Func<string, bool> hasVoice)
            => !string.IsNullOrWhiteSpace(phrase) && !hasVoice(phrase);

        /// <summary>True when at least one line of the list would stay silent (drives the hint line).</summary>
        public static bool AnySilent(IEnumerable<string>? phrases, Func<string, bool> hasVoice)
        {
            if (phrases == null) return false;
            foreach (var p in phrases)
                if (IsSilent(p, hasVoice)) return true;
            return false;
        }
    }
}
