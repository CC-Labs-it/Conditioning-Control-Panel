using System.Collections.Generic;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The head's font setup (distro-check F1/F4, decision 2026-10-10 in docs/avalonia-decisions.md).
    /// - Default family = the bundled Inter: never empty, so a system with no fonts at all starts instead
    ///   of aborting with "Default font family name can't be null or empty".
    /// - "Fredoka" resolves to the bundled Fredoka.ttf (WPF packs the same file as /Fonts/#Fredoka), so
    ///   every "Fredoka, Segoe UI" site draws the WPF display face instead of the fontconfig default.
    /// - Content emoji: the system emoji font first (keeps colour emoji on desktops that have one), then
    ///   the bundled monochrome Noto Emoji, so a minimal install shows glyphs instead of tofu.
    /// </summary>
    internal static class AppFonts
    {
        public const string Inter = "fonts:Inter#Inter";
        public const string Fredoka = "avares://CCP.Avalonia/Resources/fonts#Fredoka";
        public const string NotoEmoji = "avares://CCP.Avalonia/Resources/fonts#Noto Emoji";
        public const string Mono = "avares://CCP.Avalonia/Resources/emi/fonts#Noto Sans Mono";

        // Emoji blocks only, so text symbols Inter lacks (arrows, CJK, ...) still go to the system.
        private static readonly UnicodeRange EmojiRange = UnicodeRange.Parse("2300-23FF, 2600-27BF, 2B00-2BFF, 1F000-1FAFF");

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
            // WPF's "Consolas, Courier New" (66 sites) has neither face off Windows, and Avalonia never
            // resolves a generic "monospace" inside a composite family (it requires the returned family
            // name to contain the requested one), so both go to the already-bundled Noto Sans Mono.
            if (!System.OperatingSystem.IsWindows())
                mappings["Consolas"] = mappings["monospace"] = new FontFamily(Mono);
            return new FontManagerOptions
            {
                DefaultFamilyName = Inter,
                FontFamilyMappings = mappings,
                FontFallbacks = fallbacks,
            };
        }
    }
}
