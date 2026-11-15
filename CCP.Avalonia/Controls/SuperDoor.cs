using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    // Help portion of WPF Services/Super/SuperDoor.cs:14.
    internal static class SuperDoor
    {
        public static SuperEffect? ForHelp(string? id) => id switch
        {
            "FlashImages" => SuperEffect.FlickerDeck, "Video" => SuperEffect.LightsDown,
            "Subliminals" => SuperEffect.Afterglow, "BouncingText" => SuperEffect.Scrawl,
            "SpiralOverlay" => SuperEffect.Vortex, "PinkFilter" => SuperEffect.Creep,
            "BrainDrain" => SuperEffect.Undertow, "BubblePop" => SuperEffect.InnerBloom,
            _ => null,
        };
        public static string HelpId(SuperEffect effect) => "Super" + effect;
        public static HelpContent Help(SuperEffect effect) => new()
        {
            SectionId = HelpId(effect), Icon = "★",
            Title = Loc.Get("super_name_" + effect.ToString().ToLowerInvariant()),
            WhatItDoes = Loc.Get("super_twist_" + effect.ToString().ToLowerInvariant()),
        };
    }
}
