using System;
using System.Collections.Generic;
using System.Windows.Media;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// ONE 16 x 16 ICON PER CARD, for the book's side rail.
///
/// <para><b>Why a rail exists at all.</b> The book was four cards and a row of dots. At twenty-two
/// the dots stopped being navigation and became a progress bar: eight identical squares that tell
/// you where you are and nothing about where you could go, so getting to a card you half-remember
/// meant stepping through the chapter one flip at a time. The rail names the destinations. The
/// owner's word for it, 2026-08-30: "it seems kinda hard to navigate, maybe add a sidebar with the
/// little icons of the features, so we can nav easily between them".</para>
///
/// <para><b>The rail is scoped to the current tab, never the whole book.</b> Twenty-two icons in a
/// column would not fit above the pager without shrinking them past reading size, and it would put
/// the chapter boundaries somewhere the eye has to hunt for them. The tab strip already picks the
/// chapter; the rail picks the page inside it, so it never holds more than eight.</para>
///
/// <para><b>These are drawn, not loaded.</b> Same rule as the demo loops and for the same reason:
/// no bitmap ships, nothing is generated, and the house palette is the only palette. Sixteen cells
/// is small enough that a glyph has room for exactly one idea, which is a useful discipline - if an
/// icon needs a second shape to be read, the card behind it is probably two cards.</para>
///
/// <para>The buffer is cleared to zero rather than to a colour, so a glyph is transparent where it
/// is not drawn and the rail chip's own background (and its lit pink state) shows through. Do not
/// clear these to <see cref="EmiPix.Ink"/>: the selected chip would then be a pink square with a
/// dark tile sitting on it.</para>
/// </summary>
internal static class EmiBookGlyphs
{
    /// <summary>Cells per side. Drawn at 2x in the rail, so 32 device pixels at 100% scaling.</summary>
    public const int Size = EmiBookGlyphPainters.Size;

    private static Dictionary<string, ImageSource>? _cache;

    /// <summary>
    /// Every id this file draws, for the test that says the rail has no holes.
    ///
    /// <para>It reads the shared Core painter table rather than the built cache on purpose: rasterising twenty-two
    /// buffers costs a WPF imaging stack a headless test runner has no reason to spin up, and the
    /// question the test asks is whether a card was FORGOTTEN, which the table answers on its own.</para>
    /// </summary>
    public static IReadOnlyList<string> Ids => EmiBookGlyphPainters.Ids;

    /// <summary>
    /// The icon for a card id, or null if that card has none. Built once and held: a rail chip is
    /// rebuilt on every card change, and re-rasterising twenty-two buffers per flip would be work
    /// for nothing since the glyphs never animate.
    /// </summary>
    public static ImageSource? For(string id)
    {
        try
        {
            _cache ??= Build();
            return _cache.TryGetValue(id, out var src) ? src : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book glyph lookup failed for {Card}", id);
            return null;
        }
    }

    private static Dictionary<string, ImageSource> Build()
    {
        var d = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        foreach (var id in EmiBookGlyphPainters.Ids)
        {
            try
            {
                var p = new EmiPixelCanvas(Size, Size);
                var bmp = new EmiPixelBitmap(p);
                p.Clear(0);            // transparent, so the lit chip shows through
                EmiBookGlyphPainters.Draw(id, p);
                p.Commit();
                d[id] = bmp.Source;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book glyph draw failed for {Card}", id);
            }
        }
        return d;
    }

}
