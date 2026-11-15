using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The pure half of the section page header (nav rework 2026-10-06): which pills a section
    /// draws, which pill a tab lights, the section hue, keyboard wrap and the last-tab memory.
    /// Moved out of WPF's NavStripRules (Controls/NavRail/SectionTabStrip.xaml.cs) so both heads'
    /// strips read one truth; NavStripRules delegates here.
    /// </summary>
    public static class NavStripTable
    {
        /// <summary>Home is the dashboard (no strip); Settings keeps its own left pill column.</summary>
        public static bool ShowsPills(string? section) =>
            section != null && section != NavSections.Home && section != NavSections.Settings;

        /// <summary>The header (breadcrumb row) shows everywhere but Home.</summary>
        public static bool ShowsHeader(string? section) =>
            section != null && section != NavSections.Home;

        /// <summary>A pill that opens its own window asks before raising a navigation request.</summary>
        public static bool AsksBeforeOpening(NavTab tab) => tab.Kind == NavTabKind.Window;

        // ---- declutter wave 1 (2026-11-15, WPF f04bd6a52e) ----------------------------------
        // The strip holds ONE filled object, the pill of the page on screen. Every other pill is
        // icon + label on the tray until the pointer (or a press) asks for its face.

        /// <summary>Does a pill draw its face (plate, outline, drop band) in this state? The lit
        /// pill always does; a resting one only under the pointer or a press.</summary>
        public static bool ShowsFace(bool active, bool hovered, bool pressed) => active || hovered || pressed;

        /// <summary>The crumb's section word is the room title: larger than the page word.</summary>
        public const double CrumbTitleSize = 17;

        /// <summary>The page word's size next to it.</summary>
        public const double CrumbPageSize = 12.5;

        /// <summary>The longest text a pill's count badge shows ("5 on", "12").</summary>
        public const int BadgeTextMax = 8;

        public const double CountBadgeFontSize = 11;

        /// <summary>The count badge seam: tab key to short text ("5 on") or null for no badge.
        /// A page sets it once and calls <see cref="NotifyBadgesChanged"/> when its number moves;
        /// the strip never reaches into a page.</summary>
        public static Func<string, string?>? BadgeProvider { get; set; }

        /// <summary>Raised by <see cref="NotifyBadgesChanged"/>; a mounted strip repaints its badges.</summary>
        public static event Action? BadgesChanged;

        public static void NotifyBadgesChanged() => BadgesChanged?.Invoke();

        /// <summary>The badge text for a pill: the provider's answer, trimmed and capped at
        /// <see cref="BadgeTextMax"/>; null for no provider, a blank answer or a provider that throws.</summary>
        public static string? BadgeText(string? tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return null;
            string? text;
            try { text = BadgeProvider?.Invoke(tabKey); }
            catch { return null; }
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            return text.Length <= BadgeTextMax ? text : text.Substring(0, BadgeTextMax);
        }

        /// <summary>The pills a section draws, in table order, hidden tabs skipped.</summary>
        public static IReadOnlyList<NavTab> Pills(string? section)
        {
            if (!ShowsPills(section)) return Array.Empty<NavTab>();
            return NavSections.Find(section)?.Tabs.Where(t => !t.Hidden).ToArray() ?? Array.Empty<NavTab>();
        }

        /// <summary>
        /// The pill a tab key lights. A pill key lights itself; the permanent alias "lab" lights
        /// Eyes, where ShowTab lands it (declutter wave 2); pages that live inside a Play zone light
        /// that zone (Graded Intake and Lockdown sit in Sessions, Blink Trainer in Eyes). Null when
        /// no pill owns the page (Spiral Room).
        /// </summary>
        public static string? ActivePill(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant();
            switch (key)
            {
                case "lab": return "playeyes";
                case "gradedintake":
                case "lockdown": return "playsessions";
                case "blinktrainer": return "playeyes";
            }
            var section = NavSections.SectionForTab(key);
            return Pills(section).Any(p => p.Key == key) ? key : null;
        }

        /// <summary>The label key a breadcrumb shows for a tab (its own row in the table).</summary>
        public static string? PageLabelKey(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant() == "lab" ? "play" : tab.ToLowerInvariant();
            foreach (var t in NavSections.AllTabs)
                if (t.Key == key) return t.LabelKey;
            return null;
        }

        /// <summary>Keyboard move inside the strip, by key NAME (both heads' Key enums print
        /// "Left"/"Right"/"Home"/"End"): Left/Right wrap, Home/End jump. -1 = not a strip key.</summary>
        public static int MoveIndex(int current, int count, string key)
        {
            if (count <= 0) return -1;
            return key switch
            {
                "Left" => current <= 0 ? count - 1 : current - 1,
                "Right" => current < 0 || current >= count - 1 ? 0 : current + 1,
                "Home" => 0,
                "End" => count - 1,
                _ => -1,
            };
        }

        // Section hues (polish wave 2): one hue per section, 0xRRGGBB. The gear shares Home's lilac.
        public const uint Lilac = 0xB79CFF, Pink = 0xFF69B4, Orchid = 0xE070FF, VioletBlue = 0x7A86FF,
                          Sky = 0x5FB0FF, Coral = 0xFF9A6B, Sage = 0xA8D8A0;

        public static uint AccentRgb(string? section) => section switch
        {
            NavSections.Studio => Pink,
            NavSections.Companion => Orchid,
            NavSections.Play => VioletBlue,
            NavSections.Social => Sky,
            NavSections.You => Coral,
            NavSections.Library => Sage,
            _ => Lilac,   // Home, Settings
        };

        /// <summary>Last-tab memory: section -> tab, stored as JSON in AppSettings.NavLastTabBySection.</summary>
        public static Dictionary<string, string> ParseLastTabs(string? json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return map;
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json!);
                if (raw != null)
                    foreach (var (k, v) in raw)
                        if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v)) map[k] = v;
            }
            catch (JsonException) { }
            return map;
        }

        /// <summary>The JSON with one section's last tab set. Unchanged JSON when nothing moved.</summary>
        public static string WithLastTab(string? json, string section, string tab)
        {
            var map = ParseLastTabs(json);
            if (map.TryGetValue(section, out var had) && string.Equals(had, tab, StringComparison.OrdinalIgnoreCase))
                return json ?? string.Empty;
            map[section] = tab;
            return JsonSerializer.Serialize(map.OrderBy(p => p.Key, StringComparer.Ordinal)
                                               .ToDictionary(p => p.Key, p => p.Value));
        }

        /// <summary>The tab a section returns to: its remembered tab when the table still owns it,
        /// otherwise its default.</summary>
        public static string? LastTabFor(string? json, string section)
        {
            var def = NavSections.DefaultTab(section);
            if (ParseLastTabs(json).TryGetValue(section, out var tab)
                && string.Equals(NavSections.SectionForTab(tab), section, StringComparison.OrdinalIgnoreCase)
                && !NavSections.Redirects.ContainsKey(tab))
                return tab;
            return def;
        }
    }
}
