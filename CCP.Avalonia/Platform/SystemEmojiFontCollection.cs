using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The system colour-emoji font as its own font collection that never builds a synthetic bold/italic
    /// (decision 2026-10-12, render-all-segv in docs/avalonia-decisions.md).
    ///
    /// Avalonia 12.1.x (and master) builds a synthetic typeface by calling SkiaTypeface.TryGetStream, which copies
    /// the font out of an SKStreamAsset it neither disposes nor keeps alive. In optimised code that asset is dead
    /// during the native read, so a GC on any other thread finalizes it mid-memcpy: SIGSEGV (CI exit 139; dump:
    /// memcpy &lt;- sk_stream_read &lt;- TryGetStream &lt;- TryCreateSyntheticGlyphTypeface("emoji", Bold)). The 10 MB
    /// colour emoji font makes that copy long enough to hit regularly, and every bold text with an emoji asked for it.
    /// So bold/italic text draws its emoji at the regular face (bold: no visible difference on a colour bitmap;
    /// italic: upright instead of skewed).
    /// </summary>
    internal sealed class SystemEmojiFontCollection : FontCollectionBase
    {
        public static readonly Uri CollectionKey = new("fonts:SystemEmoji");

        /// <summary>Synthetic variants refused (test seam: proves this path, not Avalonia's, answered).</summary>
        internal int RefusedSyntheses;

        public override Uri Key => CollectionKey;

        /// <param name="font">The font file; read once, may be null (no system emoji font).</param>
        internal SystemEmojiFontCollection(Stream? font)
        {
            using (font)
                if (font is not null) TryAddGlyphTypeface(font, out _);
        }

        public override bool TryCreateSyntheticGlyphTypeface(GlyphTypeface glyphTypeface, FontStyle style, FontWeight weight,
            FontStretch stretch, [NotNullWhen(true)] out GlyphTypeface? syntheticGlyphTypeface)
        {
            syntheticGlyphTypeface = null;
            Interlocked.Increment(ref RefusedSyntheses);
            return false;   // never Avalonia's TryGetStream; the caller falls back to the nearest real face
        }

        /// <summary>The family Skia answers for "emoji", or null when there is none (or it sits inside a .ttc,
        /// which a stream-loaded typeface cannot address).</summary>
        internal static string? SystemFamilyName()
        {
            using var tf = SKFontManager.Default.MatchFamily("emoji");
            if (tf is null) return null;
            using var asset = tf.OpenStream(out var index);
            return asset is null || index != 0 ? null : tf.FamilyName;
        }

        /// <summary>The system "emoji" font file as a memory stream, or null.</summary>
        internal static Stream? OpenSystemEmoji()
        {
            using var tf = SKFontManager.Default.MatchFamily("emoji");
            if (tf is null) return null;
            using var asset = tf.OpenStream(out var index);
            if (asset is null || index != 0) return null;
            var bytes = new byte[asset.Length];
            // Inside the using: the asset outlives the copy. A short read fails closed (bundled Noto Emoji answers).
            return asset.Read(bytes, bytes.Length) == bytes.Length ? new MemoryStream(bytes, writable: false) : null;
        }
    }
}
