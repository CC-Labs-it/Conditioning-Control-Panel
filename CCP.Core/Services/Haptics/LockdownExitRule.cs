using System;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The way OUT of a Lockdown, as pure rules. Lockdown is meant to be hard to leave, never
    /// impossible: the timer always ends it and the secret phrase always works. Nothing here makes a
    /// Lockdown easier to leave than it was designed to be; each rule closes a hole where the way out
    /// silently failed (night bug hunt, 2026-10-22).
    /// </summary>
    public static class CompatLockdownExitRule
    {
        /// <summary>The secret exit phrase, as the codex and EMI's book teach it.</summary>
        public const string Phrase = "let me out";

        /// <summary>
        /// True when what the user typed is the exit phrase. Any casing, as before. Also forgiven,
        /// because each one turned a correct phrase into a "wrong phrase" tripwire:
        /// full-width letters and the ideographic space a Japanese / Chinese IME types in full-width
        /// mode (NFKC folds them to plain ASCII), a doubled space, and the period or "!" a touch
        /// keyboard adds on a double space. The words themselves never change.
        /// </summary>
        public static bool PhraseMatches(string? typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return false;
            string folded;
            try { folded = typed.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { folded = typed; }   // ill-formed UTF-16: compare it as typed

            var sb = new StringBuilder(folded.Length);
            var pendingSpace = false;
            foreach (var c in folded)
            {
                if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
            }
            var text = sb.ToString().TrimEnd('.', '!', '?', ' ');
            return string.Equals(text, Phrase, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Time left on a Lockdown clock. Every instant is UTC: the old local-time clock (DateTime.Now)
        /// jumped with daylight saving, so a lockdown running over the autumn change (03:00 back to
        /// 02:00) got a whole extra hour, and a 5-minute one started at 02:58 ran 65 minutes.
        /// Sleep or hibernate over the end time still ends it on wake (wall time keeps running).
        /// </summary>
        public static TimeSpan Remaining(DateTime activatedUtc, TimeSpan duration, DateTime nowUtc)
        {
            var left = duration - (nowUtc - activatedUtc);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>0..1 share of the clock already served (drives the Possession ladder).</summary>
        public static double ElapsedFraction(DateTime activatedUtc, TimeSpan duration, DateTime nowUtc)
        {
            if (duration <= TimeSpan.Zero) return 0;
            var f = (nowUtc - activatedUtc).TotalSeconds / duration.TotalSeconds;
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        /// <summary>
        /// Whether the Lockdown page's premium padlock veil is drawn. Never over a RUNNING lockdown:
        /// the veil is hit-test-visible and covers the whole page, so a sign-out (or any lapse of
        /// premium) mid-lockdown buried the five-click timer handle and the Emergency Exit under a
        /// padlock, with the panic key off, until the timer ran out. The veil comes back as soon as
        /// the lockdown ends. Starting a new lockdown stays premium-only (TierGate in the button).
        /// </summary>
        public static bool ShowsPremiumVeil(bool hasPremium, bool lockdownActive)
            => !hasPremium && !lockdownActive;

        /// <summary>
        /// What a recovery file that exists but cannot be read (empty, cut short by a kill mid-write,
        /// not JSON) restores. The pre-lockdown values are unknowable, and the settings on disk may
        /// still carry the lockdown's own values (panic off, strict on), so fail OPEN: panic key on,
        /// Strict Lock off. Leaving them as they were kept the panic key off for good.
        /// </summary>
        public static (bool StrictLockEnabled, bool PanicKeyEnabled) UnreadableRecovery => (false, true);
    }
}
