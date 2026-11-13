using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>One stock English text a mod's <c>textReplacements</c> can match.</summary>
    public sealed record ModTextEntry(string Group, string Text, string Source)
    {
        /// <summary>The list row: the text, with the source in brackets for context.</summary>
        public string Display => Text + "   (" + Source + ")";
    }

    /// <summary>
    /// The browsable list of original texts behind the Mod Creator's Text Replacements section.
    ///
    /// <para><b>Built from the code, never by hand.</b> Text replacement is a plain string match
    /// (<see cref="ModService.ApplyTextReplacements"/>) over whatever the UI passes through
    /// <c>MakeModAware</c>, so the useful keys are the English literals those call sites feed in:
    /// the skill tree (<see cref="SkillDefinition.All"/>), achievements, quests, rank titles,
    /// companions, presets, and the feature labels the dashboard and Studio rack probe through
    /// <c>MainWindow.ModAwareLabel</c>. Each area is read from its own definition list, so a new
    /// skill or quest shows up here with no edit. The feature labels are inline literals at their
    /// call sites; <see cref="UiLabels"/> mirrors them and <c>ModTextCatalogTests</c> scans the
    /// sources so a new label that is not listed fails the build.</para>
    ///
    /// <para>Texts over 200 characters are left out: the loader refuses a key that long.</para>
    /// </summary>
    public static class ModTextCatalog
    {
        public const string Skills = "Skills";
        public const string Flavour = "Flavour";
        public const string Achievements = "Achievements";
        public const string Quests = "Quests";
        public const string Levels = "Levels";
        public const string Companions = "Companions";
        public const string Presets = "Presets";
        public const string Other = "Other";

        /// <summary>Display order of the groups.</summary>
        public static readonly IReadOnlyList<string> Groups =
            new[] { Skills, Flavour, Achievements, Quests, Levels, Companions, Presets, Other };

        /// <summary>Same cap as the text replacement key check in <c>ModService.SanitizeManifest</c>.</summary>
        internal const int MaxKeyLength = 200;

        /// <summary>
        /// Feature and section labels probed by <c>ML</c> / <c>MLCard</c> in
        /// <c>MainWindow.UiUpdates.cs</c> and by the Studio rack rows (<c>StudioTabView.BuildRack</c>).
        /// Kept in step with the sources by <c>ModTextCatalogTests</c>.
        /// </summary>
        internal static readonly string[] UiLabels =
        {
            "Flash Images", "Flashes", "Subliminals", "Bouncing Text", "Bouncing", "Bubble Pop", "Bubbles",
            "Bubble Count", "Lock Card", "Lock Cards", "Mandatory Video", "Mandatory Videos", "Videos",
            "Spiral Overlay", "Pink Filter", "Brain Drain", "Mind Wipe", "Mindwipe", "The Vault",
            "Visuals", "Wallpaper", "Haptics", "Scheduler", "Intensity Ramp",
            "⚡ Flash Images", "🎬 Mandatory Videos", "💭 Subliminals", "🖼 Corner GIF",
            "Bimbos online now (Hive Mind skill)", "Total conditioning time (Pink Hours skill)",
            "Your rank percentile (Popular Girl skill)",
        };

        /// <summary>
        /// Every matchable text, grouped and in display order. A text that appears in two areas is
        /// listed once, under the first (the replacement is global either way). An area whose
        /// definitions cannot be read is skipped rather than failing the list.
        /// </summary>
        public static IReadOnlyList<ModTextEntry> Build()
        {
            var list = new List<ModTextEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(string group, string? text, string source)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                text = text.Trim();
                if (text.Length > MaxKeyLength) return;
                if (seen.Add(text)) list.Add(new ModTextEntry(group, text, source));
            }

            void Area(Action read)
            {
                try { read(); }
                catch (Exception ex) { App.Logger?.Debug("ModTextCatalog: an area was skipped: {E}", ex.Message); }
            }

            Area(() =>
            {
                foreach (var s in SkillDefinition.All) Add(Skills, s.Name, "skill name");
                foreach (var s in SkillDefinition.All) Add(Skills, s.Description, "skill effect");
                foreach (var s in SkillDefinition.All) Add(Skills, s.SecretRequirementDesc, "secret skill hint");
            });
            Area(() =>
            {
                foreach (var s in SkillDefinition.All) Add(Flavour, s.FlavorText, "skill flavour");
                foreach (var a in Achievement.All.Values) Add(Flavour, a.FlavorText, "achievement flavour");
            });
            Area(() =>
            {
                foreach (var a in Achievement.All.Values) Add(Achievements, a.Name, "achievement name");
                foreach (var a in Achievement.All.Values) Add(Achievements, a.Requirement, "achievement requirement");
            });
            Area(() =>
            {
                foreach (var q in QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests))
                {
                    Add(Quests, q.Name, "quest name");
                    Add(Quests, q.Description, "quest description");
                }
            });
            Area(() =>
            {
                foreach (var t in RankTitles.All) Add(Levels, t.Title, $"rank title, level {t.FromLevel}+");
            });
            Area(() =>
            {
                foreach (var c in CompanionDefinition.AllCompanions)
                {
                    Add(Companions, c.Name, "companion name");
                    Add(Companions, c.SlutModeName, "companion name (Slut Mode)");
                    Add(Companions, c.Description, "companion description");
                    Add(Companions, c.XPMechanicDescription, "companion XP bonus");
                }
            });
            Area(() =>
            {
                foreach (var p in Preset.GetDefaultPresets())
                {
                    Add(Presets, p.Name, "preset name");
                    Add(Presets, p.Description, "preset description");
                }
            });
            Area(() =>
            {
                foreach (var p in PersonalityPresets.GetAllBuiltIn())
                {
                    Add(Presets, p.Name, "personality name");
                    Add(Presets, p.Description, "personality description");
                }
            });
            Area(() =>
            {
                foreach (var l in UiLabels) Add(Other, l, "feature label");
            });

            return list
                .OrderBy(e => IndexOfGroup(e.Group))
                .ToList();
        }

        private static int IndexOfGroup(string group)
        {
            for (var i = 0; i < Groups.Count; i++)
                if (Groups[i] == group) return i;
            return Groups.Count;
        }

        /// <summary>
        /// Search used by the browser: every word of the query must appear (ignoring case) in the
        /// text, the source or the group name. An empty query matches everything.
        /// </summary>
        public static bool Matches(ModTextEntry entry, string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Text.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Source.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Group.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The "Export list" file: a short how-to, then one section per group with one original
        /// text per line, exactly as it must be typed into a replacement.
        /// </summary>
        public static string ToExportText(IReadOnlyList<ModTextEntry> entries, string? appVersion)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Conditioning Control Panel - original texts for mod text replacements");
            if (!string.IsNullOrWhiteSpace(appVersion)) sb.AppendLine("App version: " + appVersion);
            sb.AppendLine($"{entries.Count} texts. Matching is exact and case-sensitive; longer texts are replaced first.");
            sb.AppendLine("Copy a whole line into the left box of a replacement in the Mod Creator, or use it as a key in");
            sb.AppendLine("\"textReplacements\" in mod.json. Any other text shown in the app can be matched too.");

            foreach (var group in Groups)
            {
                var rows = entries.Where(e => e.Group == group).ToList();
                if (rows.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"== {group} ({rows.Count}) ==");
                foreach (var e in rows) sb.AppendLine(e.Text);
            }
            return sb.ToString();
        }
    }
}
