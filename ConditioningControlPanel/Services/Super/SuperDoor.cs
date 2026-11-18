using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The one door to a feature's V2 (its Super effect) from outside the feature panel: the cut
    /// corner on a dashboard tile, the "See V2" button in a help card and the "?" in the gold box
    /// all come through here, so they agree on which effect a feature owns, what its help says
    /// and what a press does.
    /// </summary>
    public static class SuperDoor
    {
        /// <summary>The effect a base feature owns, by the feature's help topic id. Null = no V2.</summary>
        public static SuperEffect? ForHelp(string? sectionId) => sectionId switch
        {
            "FlashImages" => SuperEffect.FlickerDeck,
            "Video" => SuperEffect.LightsDown,
            "Subliminals" => SuperEffect.Afterglow,
            "BouncingText" => SuperEffect.Scrawl,
            "SpiralOverlay" => SuperEffect.Vortex,
            "PinkFilter" => SuperEffect.Creep,
            "BrainDrain" => SuperEffect.Undertow,
            "BubblePop" => SuperEffect.InnerBloom,
            _ => null,
        };

        /// <summary>The help topic (and help loop) id of an effect: "Super" + its name.</summary>
        public static string HelpId(SuperEffect effect) => "Super" + effect;

        public static string Twist(SuperEffect effect) => Loc.Get("super_twist_" + effect.ToString().ToLowerInvariant());

        /// <summary>The V2 help card: the effect's own loop, its name and what it does.</summary>
        public static HelpContent Help(SuperEffect effect) => new()
        {
            SectionId = HelpId(effect),
            Icon = "★",
            Title = SuperNames.Name(effect),
            WhatItDoes = Twist(effect),
        };

        /// <summary>
        /// A press on a V2 switch. A free account gets the normal tier refusal and nothing flips;
        /// everyone else flips the effect. Returns true when the switch changed.
        /// </summary>
        public static bool Press(SuperEffect effect)
        {
            if (!SuperAccess.IsUnlocked(effect))
            {
                TierGate.DemandPremium(SuperNames.Name(effect));
                return false;
            }
            SuperAccess.Set(effect, !SuperAccess.IsSwitchedOn(effect));
            return true;
        }
    }
}
