using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>The dashboard entry to a feature's Super effect, matching the WPF door.</summary>
public static class SuperDoor
{
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

    public static string Twist(SuperEffect effect) => Loc.Get("super_twist_" + effect.ToString().ToLowerInvariant());

    /// <summary>WPF SuperDoor.Help: the V2 help card, id "Super" + effect (its help loop's id too).
    /// The star is the header icon the box passes, not an emoji here.</summary>
    public static ConditioningControlPanel.Models.HelpContent Help(SuperEffect effect) => new()
    {
        SectionId = "Super" + effect,
        Title = Loc.Get("super_name_" + effect.ToString().ToLowerInvariant()),
        WhatItDoes = Twist(effect),
    };

    public static bool Press(SuperEffect effect)
    {
        if (!SuperAccess.IsUnlocked(effect))
        {
            TierGate.DemandPremium(Loc.Get("super_name_" + effect.ToString().ToLowerInvariant()));
            return false;
        }
        SuperAccess.Set(effect, !SuperAccess.IsSwitchedOn(effect));
        return true;
    }
}
