using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// The rules every AI effect command passes before it runs, top level or nested inside a
    /// getbacktome (night hunt 2026-10-22). Model output is untrusted: the user's chat or a line
    /// injected into fetched content can steer it, so an AI command gets no more than the
    /// user's own switches allow.
    ///
    /// <para>1. A getbacktome ran its nested commands straight through <see cref="CommandFactory"/>,
    /// skipping the master switch, the per-effect switches and the per-reply cap: an AI
    /// that may only "get back to you" could still put up a lock card or a video the user had
    /// switched off.</para>
    ///
    /// <para>2. Nothing AI-driven listened to a panic. A follow-up scheduled up to ten minutes
    /// earlier still fired after the panic and could start the spiral, flashes or a lock card
    /// again; an unprompted Takeover comment could do the same inside the five-minute quiet.
    /// AI effects now stay off for the post-panic quiet (<see cref="Safety.PanicAftermath"/>), and
    /// a follow-up that a panic overtook while it waited never fires.</para>
    ///
    /// <para>3. Text the model puts INSIDE an effect (a subliminal, a lock card phrase the user
    /// has to type, bouncing words, a follow-up line) never met output moderation, which only
    /// read the chat reply. It now passes the same guard.</para>
    ///
    /// <para>4. The depth cap on getbacktome reset every time the follow-up's own reply carried a
    /// new getbacktome (the reply path dispatched at depth 0), so one command could keep the
    /// model calling itself every second forever. The chain depth now rides along the async
    /// flow of the follow-up's reply.</para>
    /// </summary>
    internal static class AiCommandAdmission
    {
        internal enum Verdict { Run, NoData, MasterOff, EffectOff, PanicQuiet, TextBlocked }

        /// <summary>Pure decision for one command. <paramref name="textBlocked"/> is the output
        /// moderation check on one piece of on-screen text.</summary>
        internal static Verdict Decide(AiCommandData? command, bool masterOn, bool effectAllowed, bool panicQuiet,
            Func<string, bool> textBlocked)
        {
            if (command?.Data == null) return Verdict.NoData;
            if (!masterOn) return Verdict.MasterOff;
            if (!effectAllowed) return Verdict.EffectOff;
            if (panicQuiet) return Verdict.PanicQuiet;
            foreach (var text in DisplayTexts(command))
                if (textBlocked(text)) return Verdict.TextBlocked;
            return Verdict.Run;
        }

        /// <summary>Every piece of model-written text a command would put on screen. A
        /// getbacktome's nested commands are judged on their own when they fire.</summary>
        internal static IEnumerable<string> DisplayTexts(AiCommandData command)
        {
            switch (command.Data)
            {
                case Subliminal s when !string.IsNullOrWhiteSpace(s.Text):
                    yield return s.Text;
                    break;
                case MantraLockscreen m when !string.IsNullOrWhiteSpace(m.Mantra):
                    yield return m.Mantra;
                    break;
                case Bounce b when b.Words != null:
                    foreach (var w in b.Words)
                        if (!string.IsNullOrWhiteSpace(w)) yield return w;
                    break;
                case GetBackToMe g when !string.IsNullOrWhiteSpace(g.Text):
                    yield return g.Text!;
                    break;
            }
        }

        // ---- follow-up chain depth ----

        private static readonly AsyncLocal<int> Depth = new();

        /// <summary>How deep in a getbacktome chain the current async flow is (0 = a fresh reply).</summary>
        internal static int FollowUpDepth => Depth.Value;

        /// <summary>Marks the rest of the calling async flow (the follow-up's own AI reply and
        /// the commands it dispatches) as running at <paramref name="depth"/>. Never leaks to
        /// the caller's flow.</summary>
        internal static void EnterFollowUp(int depth) => Depth.Value = Math.Max(0, depth);

        /// <summary>A follow-up still fires only when no panic was pressed while it waited.</summary>
        internal static bool FollowUpStands(long panicsAtSchedule, long panicsNow) => panicsAtSchedule == panicsNow;

        // ---- bouncing words ----

        public const int MaxBounceWords = 12;
        public const int MaxBounceWordChars = 40;

        /// <summary>The words an AI bounce may show: trimmed, non-empty, at most
        /// <see cref="MaxBounceWords"/> of at most <see cref="MaxBounceWordChars"/> characters.
        /// Null when nothing is left, so the user's own words play.</summary>
        internal static List<string>? BounceWords(IEnumerable<string>? words)
        {
            if (words == null) return null;
            var list = words.Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim())
                .Select(w => w.Length > MaxBounceWordChars ? w.Substring(0, MaxBounceWordChars) : w)
                .Take(MaxBounceWords)
                .ToList();
            return list.Count == 0 ? null : list;
        }

        // ---- named media paths ----

        /// <summary>TRUE when <paramref name="fullPath"/> sits INSIDE <paramref name="root"/>. A bare
        /// prefix test let "assets-old\x.mp4" pass for the root "assets".</summary>
        internal static bool IsUnderRoot(string root, string fullPath)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(fullPath)) return false;
            var r = root.TrimEnd('\\', '/');
            if (r.Length == 0 || fullPath.Length <= r.Length) return false;
            if (!fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return false;
            var next = fullPath[r.Length];
            return next == '\\' || next == '/';
        }

        /// <summary>TRUE when the user switched this asset (or a folder above it) off: an AI that
        /// names it by path must not play what the random picker would never draw.</summary>
        internal static bool IsSwitchedOff(string relativePath, IEnumerable<string>? disabledPaths, IEnumerable<string>? disabledFolders)
        {
            static string Norm(string p) => p.Replace('\\', '/');
            var rel = Norm(relativePath);
            if (disabledPaths != null && disabledPaths.Any(p => string.Equals(Norm(p), rel, StringComparison.OrdinalIgnoreCase)))
                return true;
            return AssetFolderExclusion.IsUnderAny(rel, disabledFolders);
        }
    }
}
