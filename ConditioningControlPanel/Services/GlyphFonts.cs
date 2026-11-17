namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Font chains for single-glyph text. Two kinds of glyph, two chains, never mixed:
    ///
    /// <para><see cref="Icon"/> is for PRIVATE-USE characters (U+E000..U+F8FF), the icon font's
    /// own drawings. <see cref="Symbol"/> is for ordinary Unicode symbols: a star, a tick.</para>
    ///
    /// <para>Segoe MDL2 Assets is a symbol-encoded font, so WPF treats it as having every
    /// character and never falls through to the next family: an ordinary symbol asked of the
    /// <see cref="Icon"/> chain draws as an empty box. That is how the V2 star shipped in 7.3.0
    /// as a hollow frame. <c>GlyphFontTests</c> scans the source for the pairing.</para>
    /// </summary>
    public static class GlyphFonts
    {
        /// <summary>Icon-font drawings (private-use characters only).</summary>
        public const string Icon = "Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI";

        /// <summary>Ordinary Unicode symbols: stars, ticks, arrows.</summary>
        public const string Symbol = "Segoe UI Symbol, Segoe UI";
    }
}
