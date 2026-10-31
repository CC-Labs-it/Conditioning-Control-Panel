using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// What player-facing text calls the V2 avatar (owner, 2026-10-30): "Rain" is only the 3D model's
    /// name, so copy uses the active mod's chosen companion name instead: Bambi on Bambi Sleep, Bimbo on
    /// Sissy Hypno (the name its "Ask your Bimbo" / "Bimbo Takeover" labels already use), Emi on CCP
    /// Default and anywhere else (web and phone hosts with no mod fall back to Emi too).
    /// Loc strings write the <c>{companion}</c> token (<see cref="ConditioningControlPanel.Localization.VocabTokens"/>); ids,
    /// grants, settings and file names keep "rain".
    /// </summary>
    public static class CompanionName
    {
        /// <summary>CCP Default's name, and the answer for any mod without its own.</summary>
        public const string Fallback = "Emi";

        /// <summary>The name for one mod id.</summary>
        public static string ForMod(string? modId)
        {
            if (string.Equals(modId, BuiltInMods.BambiSleepId, StringComparison.OrdinalIgnoreCase)) return "Bambi";
            if (string.Equals(modId, BuiltInMods.SissyHypnoId, StringComparison.OrdinalIgnoreCase)) return "Bimbo";
            return Fallback;
        }

        /// <summary>The name for the active mod; Emi when the mod layer is not up yet or throws.</summary>
        public static string ForActiveMod()
        {
            try { return ForMod(CoreMods.ActiveModId); }
            catch { return Fallback; }
        }
    }
}
