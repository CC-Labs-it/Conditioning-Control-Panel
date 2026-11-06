using System.Collections.Generic;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The head's font setup (distro-check F1/F4, decision 2026-10-10 in docs/avalonia-decisions.html).
    /// - Default family = the bundled Inter: never empty, so a system with no fonts at all starts instead
    ///   of aborting with "Default font family name can't be null or empty".
    /// - "Fredoka" resolves to static Light..Bold instances of WPF's variable Fredoka.ttf (/Fonts/#Fredoka), so
    ///   every "Fredoka, Segoe UI" site draws the WPF display face instead of the fontconfig default.
    /// - Content emoji: the system emoji font first (keeps colour emoji on desktops that have one), then
    ///   the bundled monochrome Noto Emoji, so a minimal install shows glyphs instead of tofu.
    /// </summary>
    internal static class AppFonts
    {
        public const string Inter = "fonts:Inter#Inter";
        public const string Fredoka = "avares://CCP.Avalonia/Resources/fonts/fredoka#Fredoka";
        public const string NotoEmoji = "avares://CCP.Avalonia/Resources/fonts#Noto Emoji";
        public const string Mono = "avares://CCP.Avalonia/Resources/emi/fonts#Noto Sans Mono";
        // Off-Windows stand-ins for the Windows faces WPF names (2026-11-06, owner: "bundle free look-alikes").
        public const string Selawik = "avares://CCP.Avalonia/Resources/fonts/selawik#Selawik";     // Segoe UI
        public const string Anton = "avares://CCP.Avalonia/Resources/fonts/display#Anton";          // Impact
        public const string ComicNeue = "avares://CCP.Avalonia/Resources/fonts/display#Comic Neue"; // Comic Sans MS

        /// <summary>The never-empty default face: bundled Inter on Windows (2026-10-10), bundled Selawik elsewhere
        /// (WPF draws unstyled text in Segoe UI, which Linux lacks; 2026-11-06).</summary>
        public static string Default => System.OperatingSystem.IsWindows() ? Inter : Selawik;

        // Emoji blocks only, so text symbols Inter lacks (arrows, CJK, ...) still go to the system.
        private static readonly UnicodeRange EmojiRange = UnicodeRange.Parse("2300-23FF, 2600-27BF, 2B00-2BFF, 1F000-1FAFF");
        // Selawik is Latin-only; Segoe UI (what WPF draws) covers Greek and Cyrillic, so does the bundled Inter,
        // instead of whatever fontconfig picks (the ru.json UI).
        private static readonly UnicodeRange GreekCyrillicRange = UnicodeRange.Parse("0370-03FF, 0400-052F, 1C80-1C8F, 1F00-1FFF");

        /// <summary>Registers the system emoji collection the default <see cref="Options"/> falls back to.</summary>
        public static void Register(FontManager fonts) =>
            fonts.AddFontCollection(new SystemEmojiFontCollection(SystemEmojiFontCollection.OpenSystemEmoji()));

        /// <param name="systemEmoji">false pins the bundled emoji ahead of the system (render locks).</param>
        public static FontManagerOptions Options(bool systemEmoji = true)
        {
            var fallbacks = new List<FontFallback>();
            // Not the bare system "emoji" family: bold/italic emoji there go through Avalonia's synthetic-typeface
            // copy that can SIGSEGV under GC (SystemEmojiFontCollection). Same font, loaded by our own collection.
            if (systemEmoji && SystemEmojiFontCollection.SystemFamilyName() is { } emoji)
                fallbacks.Add(new FontFallback { FontFamily = new FontFamily(SystemEmojiFontCollection.CollectionKey + "#" + emoji), UnicodeRange = EmojiRange });
            fallbacks.Add(new FontFallback { FontFamily = new FontFamily(NotoEmoji), UnicodeRange = EmojiRange });
            var mappings = new Dictionary<string, FontFamily> { ["Fredoka"] = new FontFamily(Fredoka) };
            // Every link in WPF's Font.Mono chain (App.xaml) needs a portable mapping. Avalonia
            // cannot resolve generic "monospace" inside a composite family (the returned family's
            // name must contain the requested one). Bundled Noto Sans Mono serves both cases.
            var windows = System.OperatingSystem.IsWindows();
            if (!windows)
            {
                mappings["Cascadia Mono"] = mappings["Consolas"] = mappings["Courier New"] =
                    mappings["monospace"] = new FontFamily(Mono);
                // WPF's display faces, none of which Linux has: without these "Impact, Arial Black, Segoe UI"
                // (marquee, bubble count, video overlay) and every "Segoe UI" site drew Inter or a fontconfig pick.
                mappings["Segoe UI"] = mappings["Segoe UI Semibold"] = mappings["Segoe UI Light"] = new FontFamily(Selawik);
                mappings["Impact"] = new FontFamily(Anton);
                mappings["Comic Sans MS"] = mappings["Comic Sans"] = new FontFamily(ComicNeue);
                // Heavy grotesques: Inter has the 900 weight Selawik lacks.
                mappings["Segoe UI Black"] = mappings["Arial Black"] = new FontFamily(Inter);
                fallbacks.Add(new FontFallback { FontFamily = new FontFamily(Inter), UnicodeRange = GreekCyrillicRange });
            }
            return new FontManagerOptions
            {
                // WPF draws unstyled text in the system UI face (Segoe UI); off Windows that is Selawik. Windows keeps
                // the 2026-10-10 Inter default. Both are bundled, so a font-less system still starts (distro-check F1).
                DefaultFamilyName = Default,
                FontFamilyMappings = mappings,
                FontFallbacks = fallbacks,
            };
        }
    }
}
