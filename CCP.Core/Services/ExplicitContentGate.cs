using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum — age + content acknowledgement gate logic.
    ///
    /// Centralizes the decision "does activating this preset (or flipping SlutMode on with
    /// this preset active) require the user to clear the explicit-content acknowledgement
    /// dialog?". The defense is wrapping (acknowledgement before activation), not
    /// sterilization of the underlying preset text.
    ///
    /// Rules:
    /// 1. Preset opts in unconditionally via <see cref="PersonalityPreset.RequiresExplicitAcknowledgement"/>
    ///    (currently only the "slutmode" built-in).
    /// 2. Slut Mode ON gates EVERY preset (2026-10-16). The SlutModeBite block rides on every
    ///    persona while the switch is on, so every persona is explicit while it is on, whether
    ///    or not it ships its own SlutModePersonality. Before this only presets with a variant
    ///    were gated, which matched the old behaviour where the others did not change at all.
    /// </summary>
    public static class ExplicitContentGate
    {
        /// <summary>
        /// True if activating <paramref name="preset"/> (with the supplied SlutMode state)
        /// requires the acknowledgement dialog. Pass <paramref name="slutModeOn"/> = the
        /// SlutMode state that WILL be in effect after the pending action.
        /// </summary>
        public static bool RequiresAcknowledgement(PersonalityPreset? preset, bool slutModeOn)
        {
            if (preset == null) return false;

            // Rule 1: unconditional preset opt-in (e.g. SlutMode built-in).
            if (preset.RequiresExplicitAcknowledgement) return true;

            // Rule 2: the switch itself is explicit on every persona now (SlutModeBite).
            return slutModeOn;
        }

        /// <summary>
        /// True if the user's current <see cref="CompanionPromptSettings.ExplicitContentAcknowledged"/>
        /// + <see cref="CompanionPromptSettings.ExplicitAcknowledgedVersion"/> already satisfy the
        /// current acknowledgement version constant.
        /// </summary>
        public static bool IsAlreadyAcknowledged(CompanionPromptSettings? settings)
        {
            if (settings == null) return false;
            return settings.ExplicitContentAcknowledged
                   && settings.ExplicitAcknowledgedVersion == CompanionPromptSettings.ExplicitAcknowledgementVersion;
        }

        /// <summary>
        /// Persists an Accept on the explicit-content dialog. Caller is responsible for
        /// calling SettingsService.Save() after this. Idempotent.
        /// </summary>
        public static void MarkAcknowledged(CompanionPromptSettings settings)
        {
            settings.ExplicitContentAcknowledged = true;
            settings.ExplicitAcknowledgedVersion = CompanionPromptSettings.ExplicitAcknowledgementVersion;
        }
    }
}
