using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// ccp-bugs #1547 (7.2.1): "Strict lock on lock card doesn't work - always allows me to press
    /// escape". Not a lost strict flag: every trigger (scheduler, autonomy, voice command, remote,
    /// leash, mantra command, test button) reaches <c>LockCardWindow</c> through
    /// <c>strict = customStrict || settings.LockCardStrict</c>, and a strict card swallows its own
    /// Escape while a panic escape is live. But Escape is the DEFAULT panic key, and an open card
    /// outranks every panic rung (<c>PanicPolicy.Rung.DismissLockCard</c>), so with the default
    /// binding the press that the card swallows is also the panic press that ends it. That is hard
    /// rule 6 (panic is never weaker than a stop gesture) and stays. What was wrong is the copy:
    /// the confirm said "You will NOT be able to escape lock cards with ESC" and the card hint said
    /// "only the panic key (Escape) closes this", which read as "Escape does nothing".
    ///
    /// <para>Pure, so the choice of words is unit tested without a window or settings.</para>
    /// </summary>
    internal static class LockCardStrictCopy
    {
        internal const string HintEscCloses = "label_press_esc_to_close";
        internal const string HintOnlyPanicKey = "label_strict_only_panic_key_closes";
        internal const string HintPanicIsEscape = "label_strict_panic_is_escape";

        internal const string ConfirmTitle = "lockcard_strict_confirm_title";
        internal const string ConfirmBody = "lockcard_strict_confirm_body";
        internal const string ConfirmBodyPanicIsEscape = "lockcard_strict_confirm_body_esc";

        /// <summary>Is the panic key bound to Escape (the default)? Same test as
        /// <c>PanicPolicy.GameClaimsEscapeAsPause</c>.</summary>
        internal static bool PanicIsEscape(string? panicKey)
            => string.Equals(panicKey?.Trim(), "Escape", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The bottom hint on a card. <paramref name="escClosesCard"/> is the window's live answer
        /// (non-strict, or no live panic escape: Esc is the ordinary dismiss). A strict card with a
        /// live panic key names that key; when the key IS Escape it says plainly that Escape still
        /// ends the card, because it does.
        /// </summary>
        internal static string HintKey(bool escClosesCard, string? panicKey)
        {
            if (escClosesCard) return HintEscCloses;
            return PanicIsEscape(panicKey) ? HintPanicIsEscape : HintOnlyPanicKey;
        }

        /// <summary>The body of the "turn Strict on" confirm. Never promises Escape does nothing
        /// while Escape is the panic key.</summary>
        internal static string ConfirmBodyKey(string? panicKey)
            => PanicIsEscape(panicKey) ? ConfirmBodyPanicIsEscape : ConfirmBody;
    }
}
