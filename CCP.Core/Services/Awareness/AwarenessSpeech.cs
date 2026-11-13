using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The parsed result of one awareness LLM call.
    /// </summary>
    /// <param name="Line">The line to speak. Empty whenever <see cref="Passed"/> is true or nothing usable came back.</param>
    /// <param name="Callback">
    /// The optional past-tense variant the model was invited to add (doc 02 §4.3). The arbiter speaks
    /// this one instead when the user has already moved on by the time the reply lands — the delivery
    /// re-tag, rather than dropping the line or delivering a stale one.
    /// </param>
    /// <param name="Passed">The model answered <c>[PASS]</c>: it had nothing good, and we honour that.</param>
    /// <param name="IsAiGenerated">True only for genuine model text. Same badge semantics as everywhere else.</param>
    /// <param name="Refusal">Non-null when the moderation spine blocked the call in either direction.</param>
    /// <param name="Reason">Log-safe outcome token: <c>ok</c>, <c>pass</c>, <c>refused</c>, <c>no-text</c>, …</param>
    public sealed record AwarenessReaction(
        string Line,
        string? Callback,
        bool Passed,
        bool IsAiGenerated,
        ModerationRefusalInfo? Refusal,
        string Reason)
    {
        /// <summary>Nothing to say, for the stated reason. Never a delivery, so no cooldown is burnt.</summary>
        public static AwarenessReaction None(string reason) =>
            new(string.Empty, null, Passed: false, IsAiGenerated: false, Refusal: null, Reason: reason);

        /// <summary>The model declined, on purpose. Distinct from a failure so the arbiter can refund the slot.</summary>
        public static AwarenessReaction Pass { get; } =
            new(string.Empty, null, Passed: true, IsAiGenerated: true, Refusal: null, Reason: "pass");

        /// <summary>True when there is a line worth putting in a bubble.</summary>
        public bool HasLine => !Passed && Line.Length > 0;
    }

    /// <summary>
    /// The companion's mouth, as the arbiter sees it. Two ways to speak and one question:
    /// "what is on screen right now?", which is the only thing that can tell a fresh line from a
    /// stale one (doc 02 §4.3).
    ///
    /// <para>It is an interface because the whole "exactly one reaction per frame" guarantee is a
    /// counting argument, and a counting argument that can only be checked by watching a speech bubble
    /// is not one anybody will check. Every decision-table row in
    /// <c>AwarenessArbiterDecisionTests</c> counts calls on a fake of this.</para>
    /// </summary>
    public interface IAwarenessSpeaker
    {
        /// <summary>
        /// The app id in the foreground NOW, or null when it cannot be determined.
        ///
        /// <para><b>Null means "unknown", not "different".</b> The staleness rule drops a line only
        /// when it can prove the user has moved on; an unknowable foreground is the normal case for an
        /// unclassified window, and dropping every line for it would be a silent mute.</para>
        /// </summary>
        string? CurrentAppId { get; }

        /// <summary>
        /// Speaks a canned, voiced, free bark for this frame through the existing BarkService path —
        /// including its own gates, which stay in force (BarkService's 60s global min-gap remains the
        /// outer floor for non-safety lines).
        /// </summary>
        /// <returns>True only if a line actually reached the user.</returns>
        bool TrySpeakBark(ContextFrame frame);

        /// <summary>
        /// Speaks a model-written line through the existing speech-bubble entry point (priority bubble,
        /// AI badge, double-bounce for the rare tiers) and mutes it for OCR/keyword matching.
        /// </summary>
        /// <returns>True only if the line was handed to the bubble path.</returns>
        bool TrySpeakLine(string line, RarityTier tier);
    }

    /// <summary>
    /// Where an awareness line comes from. Implemented in production by
    /// the head line source.
    ///
    /// <para>It does NOT route through the brain reaction method — the reaction prompt is its own
    /// small one, sent straight to <c>IAiService.SendAsync</c>, which is where the moderation spine
    /// lives and therefore still applies unchanged. What that path does not give it for free is the
    /// brain's single-flight gate and its turn log, so it asks <c>CompanionBrain.IsBusy</c> itself and
    /// stands down when a chat call is in flight (ambient requests are dropped when busy), and an
    /// awareness line does not land in the chat thread. Train 4's memory seam reconnects the latter.</para>
    /// </summary>
    public interface IAwarenessLineSource
    {
        /// <summary>Whether an LLM line could be produced at all right now (entitlement, provider, kill switches).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Asks for one line about this frame. Must never throw for an ordinary failure — return
        /// <see cref="AwarenessReply.Empty"/> and let the arbiter fall back to a bark.
        /// </summary>
        Task<AwarenessReply> RequestAsync(ContextFrame frame, CancellationToken cancellationToken);
    }

    /// <summary>
    /// What came back from the model, as the arbiter consumes it.
    ///
    /// <para><b>The response contract</b> (the prompt package owns instructing the model;
    /// the awareness parser owns honouring it, and is the ONLY parser — this
    /// type used to carry a second one that expected <c>ALT:</c> while the shipped prompt teaches
    /// <c>CALLBACK:</c>, so any caller that reached for "the obvious parser on the type that models
    /// the contract" would have folded every callback into the spoken line and silently killed the
    /// staleness re-tag):</para>
    /// <list type="bullet">
    /// <item>One line of text — what she says.</item>
    /// <item>An optional second line beginning <c>CALLBACK:</c> — the same beat written as a past-tense
    /// callback ("I saw you on X a minute ago…"), carried here as <see cref="Alternate"/>. It is used
    /// ONLY when the line arrived too late and the user has already moved on; a present-tense line
    /// about the wrong window is the single most common way this feature reads as broken (doc 02 §4.3).</item>
    /// <item><c>[PASS]</c> alone — she has nothing good. Nothing is said and nothing is spent.</item>
    /// </list>
    ///
    /// <para>Model text is untrusted in the same sense authored card text is: it is echoed into a
    /// bubble and back into later prompts as the ban list. The sanitising lives in the one parser.</para>
    /// </summary>
    public sealed record AwarenessReply(string? Line, string? Alternate, bool IsPass)
    {
        /// <summary>The silence token, as the one parser recognises it.</summary>
        public const string PassSentinel = "[PASS]";

        /// <summary>Prefix marking the stale-delivery callback line, as the shipped prompt teaches it.</summary>
        public const string CallbackPrefix = "CALLBACK:";

        /// <summary>Hard cap on a delivered line, owned by the one parser's clamp.</summary>
        public const int MaxLineLength = 140;

        /// <summary>Nothing usable came back. The arbiter falls back to a bark exactly once.</summary>
        public static readonly AwarenessReply Empty = new(null, null, false);

        /// <summary>She chose silence. Honoured, logged, and not charged to the budget.</summary>
        public static readonly AwarenessReply Pass = new(null, null, true);

        /// <summary>True when there is a primary line worth delivering.</summary>
        public bool HasLine => !string.IsNullOrWhiteSpace(Line);

        /// <summary>True when a past-tense callback variant was offered for the stale case.</summary>
        public bool HasAlternate => !string.IsNullOrWhiteSpace(Alternate);

        /// <summary>
        /// Parses raw model text through the awareness parser — the parser the
        /// production path already uses — so there is exactly one implementation of one contract.
        /// Anything it cannot make sense of becomes <see cref="Empty"/>, which costs a bark, not a crash.
        /// </summary>
        public static AwarenessReply Parse(string? raw)
        {
            var reaction = ParseReaction(raw);
            if (reaction == null) return Empty;
            if (reaction.Passed) return Pass;
            if (!reaction.HasLine) return Empty;
            return new AwarenessReply(reaction.Line, reaction.Callback, IsPass: false);
        }
        internal static AwarenessReaction ParseReaction(string? raw)
        {
            string? speaker;
            try { speaker = CoreMods.Service?.GetCompanionName(); } catch { speaker = null; }
            var cleaned = AiTextHygiene.StripMetadataTags(
                AiTextHygiene.UnwrapSpokenSigil(AiTextHygiene.Clean(raw), speaker));
            if (cleaned.Length == 0) return AwarenessReaction.None("empty");

            string? line = null;
            string? callback = null;

            foreach (var rawLine in cleaned.Replace("\r\n", "\n").Split('\n'))
            {
                var text = Unquote(rawLine.Trim());
                if (text.Length == 0) continue;

                if (text.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    callback ??= Clamp(Unquote(text.Substring(CallbackPrefix.Length).Trim()));
                    continue;
                }

                line ??= text;
            }

            // Per-field rather than over the whole reply, so the LINE:/CALLBACK: structure survives:
            // the strip drops the whole sentence carrying an unsanctioned URL, which over the raw
            // blob could take a "CALLBACK:" prefix with it. An emptied field falls back to null so
            // the callback-only path below still applies, and a reply that was nothing but an
            // invented link ends as silence — the right outcome for a line nobody can trust.
            if (line != null)
            {
                line = AiTextHygiene.StripUnsanctionedLinks(line);
                if (line.Length == 0) line = null;
            }
            if (callback != null)
            {
                callback = AiTextHygiene.StripUnsanctionedLinks(callback);
                if (callback.Length == 0) callback = null;
            }

            if (line == null)
            {
                // A reply that was nothing but a CALLBACK line still said something usable — the
                // arbiter would rather have the past-tense variant than silence.
                return callback is { Length: > 0 }
                    ? new AwarenessReaction(callback, null, false, true, null, "callback-only")
                    : AwarenessReaction.None("no-line");
            }

            if (IsPassToken(line)) return AwarenessReaction.Pass;

            var clamped = Clamp(line);
            if (clamped.Length == 0) return AwarenessReaction.None("no-line");

            return new AwarenessReaction(clamped, string.IsNullOrEmpty(callback) ? null : callback,
                Passed: false, IsAiGenerated: true, Refusal: null, Reason: "ok");
        }

        /// <summary>
        /// True for the silence token in the shapes models actually emit it in: bare, quoted, with a
        /// trailing full stop, or lowercased.
        /// </summary>
        private static bool IsPassToken(string? line)
        {
            var text = Unquote((line ?? string.Empty).Trim()).TrimEnd('.', '!', '~', ' ');
            return string.Equals(text, PassSentinel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "PASS", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Trims to <see cref="MaxLineLength"/> at a word boundary. Truncation is a last resort — the
        /// contract asks for 140 — but a clipped line still reads better than a wall of text in a
        /// speech bubble sized for one.
        /// </summary>
        internal static string Clamp(string? text)
        {
            var line = (text ?? string.Empty).Trim();
            if (line.Length <= MaxLineLength) return line;

            var cut = line.Substring(0, MaxLineLength - 1);
            int lastSpace = cut.LastIndexOf(' ');
            if (lastSpace > MaxLineLength / 2) cut = cut.Substring(0, lastSpace);
            return cut.TrimEnd() + "…";
        }

        /// <summary>Sheds one symmetric layer of wrapping quotes, which models add despite being told not to.</summary>
        internal static string Unquote(string text)
        {
            if (text.Length < 2) return text;

            char first = text[0];
            char last = text[text.Length - 1];
            bool matched =
                (first == '"' && last == '"') ||
                (first == '\'' && last == '\'') ||
                (first == '“' && last == '”') ||
                (first == '‘' && last == '’');

            return matched ? text.Substring(1, text.Length - 2).Trim() : text;
        }

    }
}
