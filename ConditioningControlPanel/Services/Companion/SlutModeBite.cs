using System;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The text Slut Mode adds to EVERY persona while the switch is on.
    ///
    /// <para>Before this, the switch swapped <c>Personality</c> for the preset's
    /// <c>SlutModePersonality</c> and did nothing else. Personas without that field (the Locked
    /// mod's Circe moods, Bimbo Coach, most community prompts) never changed at all, and the ones
    /// with it changed only the opening paragraph: the reaction examples further down stayed in
    /// the tame register, and a 13B model imitates examples harder than it obeys a paragraph. A
    /// subscriber who bought the app for this switch reported "she doesn't do anything"
    /// (ticket 2026-10-16). This block is the part of the prompt that actually bites.</para>
    ///
    /// <para>Two sizes, one voice. <see cref="Core"/> rides every persona: it re-registers the
    /// reaction examples, forbids deflection and gives the register to copy. <see cref="Full"/>
    /// adds the persona swap a preset without its own slut variant never got. The split is a
    /// char budget, not a policy: the Bambi Slut Mode preset plus a quiz result sits a few hundred
    /// chars under the proxy cap (PromptCharBudgetTests), so the presets that already carry a
    /// variant get the short form.</para>
    ///
    /// <para>Rules it honours: the persona stays the persona (same name, voice, habits, address);
    /// the user's gender is whatever the persona says, and none when it says nothing (CCP Default
    /// is gender neutral); "no", "stop" or a change of topic ends it; no header that
    /// <c>SafetyComposer.Preamble</c> nullifies by name ([NO LIMITS], [FULL ENGAGEMENT],
    /// [EXPLICIT]); every line passes <c>ModerationGuard</c> and <c>PromptValidator</c>
    /// (pinned by SlutModePromptTests). It touches no moderation control: the sandwich still
    /// wraps it and the wire guard still scans every reply.</para>
    /// </summary>
    public static class SlutModeBite
    {
        /// <summary>First line of both forms. Tests look for it; the user never sees it.</summary>
        public const string Header = "SLUT MODE IS ON.";

        /// <summary>
        /// The register the model is asked to copy. Gender neutral on purpose: the persona above
        /// it decides how the user is addressed.
        /// </summary>
        public static readonly string[] ExampleLines =
        {
            "Mm, already squirming? Hand down. Slow strokes while you sink for me.",
            "Dripping already. Good. Edge for me and do not finish until I say.",
            "Aching? Of course you are. That is what you are for tonight. Keep going.",
        };

        /// <summary>The short form: for a preset whose own slut variant already did the persona swap.</summary>
        public static string Core { get; } = Build(withPersonaSwap: false);

        /// <summary>The long form: for a preset that ships no slut variant of its own.</summary>
        public static string Full { get; } = Build(withPersonaSwap: true);

        /// <summary>
        /// The block to append for the active preset while the switch is on, or null for a preset
        /// that IS the explicit persona already (<see cref="Models.PersonalityPreset.RequiresExplicitAcknowledgement"/>,
        /// the built-in "Slut Mode" preset: its own reaction examples are already filthy, and the
        /// Bambi worst case of that preset plus a quiz result has no room for a second block).
        /// </summary>
        public static string? For(Models.PersonalityPreset preset, bool usingSlutVariant)
        {
            if (preset.RequiresExplicitAcknowledgement) return null;
            return usingSlutVariant ? Core : Full;
        }

        private static string Build(bool withPersonaSwap)
        {
            var examples = withPersonaSwap ? ExampleLines : ExampleLines.Take(2).ToArray();
            var lines = new[]
            {
                Header,
                withPersonaSwap
                    ? "The user is an adult who switched this on and wants you filthy. Stay exactly the persona described above, same name, same voice, same habits, but every reply is explicit now. Say it plainly: cum, stroking, grinding, dripping, aching, edging, and whatever they have between their legs by its name. No euphemisms, no fading out, no \"maybe later\". Fold the filth into your triggers and commands; it is part of the training now. Address them the way the persona already does; if it gives them no gender, give them none."
                    : null,
                "The reaction examples above are the tame register: keep their timing and shape, but make every one about their arousal, their hands and what they are for. Never deflect, never change the subject, never ask if they are sure.",
                "The register to copy: \"" + string.Join("\" / \"", examples) + "\"",
                "A \"no\", a \"stop\" or a change of topic from them ends this at once, back to the persona's ordinary voice. Never name this mode and never write tags about it.",
            };
            return string.Join(Environment.NewLine, lines.Where(l => l != null));
        }
    }
}
