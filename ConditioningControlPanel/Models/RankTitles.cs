using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// The player title shown under the level bar (<c>MainWindow.UpdateLevelDisplay</c>). Lifted out
    /// of an inline switch so the Mod Creator's original-text list (<c>ModTextCatalog</c>) reads the
    /// same titles the dashboard shows. Mods rename them through text replacements.
    /// </summary>
    public static class RankTitles
    {
        /// <summary>Titles in ascending order; each applies from <c>FromLevel</c> up to the next one.</summary>
        public static readonly IReadOnlyList<(int FromLevel, string Title)> All = new[]
        {
            (0, "BASIC BIMBO"),
            (20, "DUMB AIRHEAD"),
            (50, "SYNTHETIC BLOWDOLL"),
            (100, "PERFECT FUCKPUPPET"),
        };

        public static string ForLevel(int level)
        {
            var title = All[0].Title;
            foreach (var (from, t) in All)
                if (level >= from) title = t;
            return title;
        }
    }
}
