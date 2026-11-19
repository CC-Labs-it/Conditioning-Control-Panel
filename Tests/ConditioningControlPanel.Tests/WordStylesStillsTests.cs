using System;
using System.IO;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Looks;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Word styles drawn: the subliminal card's styled path (SubliminalLayer.DrawStyledCard over
/// WordStyleSkia), its faces, and the wiring from the service. Stills with CCP_WORDS_DUMP=dir.
/// </summary>
public class WordStylesStillsTests
{
    [Fact]
    public void Style_faces_fall_back_to_the_surface_face_when_none_is_installed()
    {
        var own = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);   // shared handle: never disposed here
        var none = new WordFace(new[] { "No Such Face 4242" }, true, 1, null, null, false, 0);
        Assert.Same(own, WordStyleSkia.FaceFor(none, own));
        Assert.Same(own, WordStyleSkia.FaceFor(WordStyles.FaceOf(WordStyle.Drift), own));
        Assert.Equal("Consolas", WordStyleSkia.FaceFor(WordStyles.FaceOf(WordStyle.Type), own).FamilyName);
    }

    [Fact]
    public void The_service_hands_the_card_the_worn_style()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs"));
        Assert.Contains("Looks.WornWordStyle.Current, Looks.WornWordStyle.Motion", src);
        var layer = File.ReadAllText(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Compositor", "SubliminalLayer.cs"));
        // Classic builds no styled look, so the classic card is drawn by the untouched path.
        Assert.Contains("if (style != WordStyle.Classic)", layer);
        // Gentle visuals: the word keeps its face and only fades (no caret blink, no bulbs).
        var worn = File.ReadAllText(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services", "Looks", "WornWordStyle.cs"));
        Assert.Contains("if (Safety.GentleVisuals.On) return WordMotion.Still;", worn);
    }

    /// <summary>
    /// Proof in pictures: with CCP_WORDS_DUMP=dir, one sheet per style, five frames across a 4 s
    /// card (rows) for a short word and a long phrase (columns), drawn by the card's own static.
    /// Without the variable it only checks every style draws something.
    /// </summary>
    [Fact]
    public void Dump_the_subliminal_styles()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_WORDS_DUMP");
        bool dump = !string.IsNullOrEmpty(dir);
        if (dump) Directory.CreateDirectory(dir!);
        const int CellW = 1000, CellH = 150;
        const float Scale = 0.5f;
        double life = 4.0;
        double[] at = { 0.06, 0.25, 0.5, 1.2, 3.75, 0.06 };   // the last row is motion Off
        string[] texts = { "sink", "let go and sink deeper" };
        var own = SubliminalLayer.ResolveFace(null);   // the layer's own static face: never disposed
        foreach (var style in new[] { WordStyle.Classic, WordStyle.Type, WordStyle.Chalk, WordStyle.Drift, WordStyle.Echo, WordStyle.Marquee })
        {
            using var bmp = new SKBitmap(new SKImageInfo(CellW * 2, CellH * at.Length, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bmp))
            using (var paint = new SKPaint { IsAntialias = true })
            using (var layer = new SKPaint())
            {
                canvas.Clear(new SKColor(29, 24, 38));
                var look = style == WordStyle.Classic ? new WordFace(null, true, 1, null, null, false, 0) : WordStyles.FaceOf(style);
                var face = WordStyleSkia.FaceFor(look, own);
                var text = look.Colour is uint c ? WordStyleSkia.Colour(c) : new SKColor(255, 0, 255);
                var border = look.Border is uint b ? WordStyleSkia.Colour(b) : SKColors.White;
                var f = new WordFrame();
                for (int col = 0; col < texts.Length; col++)
                {
                    var line = StyledLine.Build(WordStyles.TextFor(style, texts[col]), face, look.Bold ? SKFontStyle.Bold : SKFontStyle.Normal, 120 * Scale * (float)look.Size);
                    for (int row = 0; row < at.Length; row++)
                    {
                        double t = at[row];
                        // The card's own envelope: 50 ms in, 50 ms out of the 4 s.
                        double env = Math.Min(1, Math.Min(t / 0.05, (life - t) / 0.05));
                        var motion = row == at.Length - 1 ? WordMotion.Still : WordMotion.Full;
                        WordStyles.Fill(style, line.Count, t, life, motion, f);
                        var cell = new SKRectI(col * CellW, row * CellH, (col + 1) * CellW, (row + 1) * CellH);
                        SubliminalLayer.DrawStyledCard(canvas, cell, Scale, line, look, f, text, border, 0.8 * env, paint, layer);
                        paint.Color = new SKColor(255, 255, 255, 90); paint.TextSize = 13; paint.Typeface = null; paint.TextAlign = SKTextAlign.Left;
                        canvas.DrawText($"{style} t={t:0.00}s {motion}", cell.Left + 6, cell.Top + 16, paint);
                    }
                }
            }
            int lit = 0;
            for (int y = 0; y < bmp.Height; y += 4)
                for (int x = 0; x < bmp.Width; x += 4)
                    if (bmp.GetPixel(x, y).Red > 120) lit++;
            Assert.True(lit > 200, $"{style} drew {lit}");
            if (!dump) continue;
            using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(Path.Combine(dir!, $"sub-{(int)style}-{style.ToString().ToLowerInvariant()}.png"), data.ToArray());
        }
    }
}
