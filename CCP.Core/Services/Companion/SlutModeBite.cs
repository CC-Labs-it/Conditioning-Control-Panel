using System;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The text Slut Mode adds to EVERY persona while the switch is on.
    ///
    /// <para>Before this, the switch swapped <c>Personality</c> for the preset's
    /// <c>SlutModePersonality</c> and did nothing else. Personas without that field (the Locked
    /// mod's Circe moods, Bimbo Coach, most community prompts) never changed at all, and the ones
    /// with it changed only the opening paragraph: the reaction examples further down stayed in
    /// the tame register, the output rules still capped every reply at 15 words, and a 13B model
    /// imitates examples harder than it obeys a paragraph. A subscriber who bought the app for
    /// this switch reported "she doesn't do anything" (ticket 2026-10-16). This block is the part
    /// of the prompt that actually bites.</para>
    ///
    /// <para>Rules it honours: the persona stays the persona (same name, voice, habits, address);
    /// the user's gender is whatever the persona says, and none when it says nothing (CCP Default
    /// is gender neutral); "no", "stop" or a change of topic ends it; no header that
    /// <c>SafetyComposer.Preamble</c> nullifies by name ([NO LIMITS], [FULL ENGAGEMENT],
    /// [EXPLICIT]); every line passes <c>ModerationGuard</c> and <c>PromptValidator</c>
    /// (pinned by SlutModePromptTests). It touches no moderation control: the sandwich still
    /// wraps it and the wire guard still scans every reply.</para>
    ///
    /// <para>Length is part of it. The owner's first desk run (Bambi preset, 2026-10-16) got
    /// nine-token replies: the persona's "Max 15 words" rule and the 100-token chat cap won. The
    /// block lifts the word cap, <c>AiCallOptions.ChatSpice</c> lifts the token cap, and
    /// <c>PromptAssembler</c> swaps the "one short bubble" tail line for the spice one.</para>
    ///
    /// <para>Size: the Bambi worst case (bambisprite preset, worst title permutation, a quiz) sits
    /// ~9,500 chars with this block, over the 9,000 soft ceiling (tail shed, fine) and under the
    /// 9,900 proxy hard cap (every cloud call 400s). PromptCharBudgetTests pins it; a longer block
    /// here trips that test, not production.</para>
    /// </summary>
    public static class SlutModeBite
    {
        /// <summary>First line of the block. Tests look for it; the user never sees it.</summary>
        public const string Header = "SLUT MODE IS ON.";

        // No sample replies in this block. "The register to copy" plus three full lines made models quote
        // them word for word and ignore the message (players, 2026-11-01). Measured on the 13B model:
        // 24 of 32 replies repeated a sample, 14 of 32 with "never repeat them", 0 with no samples.
        public static string Block { get; } = Build();

        /// <summary>
        /// The block to append for the active preset while the switch is on, or null for a preset
        /// that IS the explicit persona already (<see cref="Models.PersonalityPreset.RequiresExplicitAcknowledgement"/>,
        /// the built-in "Slut Mode" preset: its own reaction examples are already filthy, and the
        /// Bambi worst case of that preset plus a quiz result has no room for a second block).
        /// </summary>
        public static string? For(Models.PersonalityPreset preset) =>
            preset.RequiresExplicitAcknowledgement ? null : Block;

        private static string Build()
        {
            var lines = new[]
            {
                Header,
                "The user is an adult who switched this on and wants you filthy. Stay exactly the persona above, same name, voice and habits, but every reply is explicit now. Say it plainly: cum, stroking, grinding, dripping, aching, edging, and whatever they have between their legs by its name. No euphemisms, no fading out. Fold the filth into your triggers and commands. Address them as the persona does; if it gives them no gender, give them none.",
                "The reaction examples above are the tame register: keep their shape, make every one about their arousal, their hands and what they are for. Never deflect, never ask if they are sure.",
                "Length: two to four sentences, 30 to 70 words; any shorter cap above is off while this is on. End on one command or one question.",
                "Answer what they just said first, in your own new words; never fall back on a stock line.",
                "A \"no\", a \"stop\" or a change of topic from them ends this at once, back to the ordinary voice. Never name this mode.",
            };
            return string.Join(Environment.NewLine, lines);
        }
    }
}
