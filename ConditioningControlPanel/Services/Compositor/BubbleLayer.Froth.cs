using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Froth drawing: the gold rim of a ripe bubble and the pictures a merged bubble carries,
/// circling each other inside the glass. Where each picture sits and how big it is comes from
/// <see cref="FrothRules.PictureSlot"/>; the bubble itself is the ordinary sprite, drawn bigger.
/// The pictures are stills owned by the item (BubbleItem.FrothPictures), never built per frame.
/// UI thread only, like the rest of the layer.
/// </summary>
public sealed partial class BubbleLayer
{
    private static readonly SKColor FrothGold = new(0xFF, 0xD7, 0x00);
    private readonly SKPaint _frothEdge = new() { IsAntialias = true, BlendMode = SKBlendMode.DstIn };

    /// <summary>A thin gold ring on the glass. Fades in and out with BubbleItem.FrothRim (the
    /// bubble eases it over 400 ms, so it never snaps on), and follows the pop scale.</summary>
    private void DrawFrothRim(SKCanvas canvas, BubbleItem item, float cx, float cy, float half)
    {
        float s = item.DpiScale;
        float r = half * (float)(FrothRules.GlassOfSprite * 2) * item.Scale;
        byte a = (byte)Math.Clamp(item.FrothRim * item.Opacity * 235f, 0f, 255f);
        _stroke.Color = FrothGold.WithAlpha((byte)(a * 0.35f));
        _stroke.StrokeWidth = 7f * s;
        canvas.DrawCircle(cx, cy, r, _stroke);
        _stroke.Color = FrothGold.WithAlpha(a);
        _stroke.StrokeWidth = 3f * s;
        canvas.DrawCircle(cx, cy, r, _stroke);
    }

    /// <summary>Every picture inside a merged bubble: its own face first (when it has one), then
    /// the stills it took in. One picture sits in the middle, more circle each other slowly.</summary>
    private void DrawFrothPictures(SKCanvas canvas, BubbleItem item, float cx, float cy, float half, byte ga)
    {
        var extra = item.FrothPictures!;
        var own = item.HasFace ? item.CurrentTeaseFrame() : null;
        int count = Math.Min(FrothRules.MaxPictures, extra.Count + (own != null ? 1 : 0));
        float glass = half * (float)(FrothRules.GlassOfSprite * 2);
        for (int i = 0; i < count; i++)
        {
            var img = own != null ? (i == 0 ? own : extra[i - 1]) : extra[i];
            var slot = FrothRules.PictureSlot(i, count, item.FrothTime);
            float px = cx + (float)slot.Dx * glass, py = cy + (float)slot.Dy * glass;
            float ir = (float)slot.Size * glass * 0.5f;
            if (ir < 1f) continue;
            var box = new SKRect(px - ir, py - ir, px + ir, py + ir);
            int clip = canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(box, ir, ir), antialias: true);
            int layer = canvas.SaveLayer(box, null);
            _fill.Shader = null;
            _fill.Color = new SKColor(0x14, 0x07, 0x0C, ga);
            canvas.DrawCircle(px, py, ir, _fill);
            _img.Color = SKColors.White.WithAlpha((byte)(0.92f * item.Opacity * 255f));
            canvas.DrawImage(img, FillFit(img, box), _img);
            // The same soft edge the single face has, so a picture melts into the glass.
            using (var edge = SKShader.CreateRadialGradient(new SKPoint(px, py), ir,
                new[] { SKColors.White, SKColors.White, SKColors.Transparent },
                new[] { 0f, BubbleFace.EdgeFadeStart, 1f }, SKShaderTileMode.Clamp))
            {
                _frothEdge.Shader = edge;
                canvas.DrawRect(box, _frothEdge);
                _frothEdge.Shader = null;
            }
            canvas.RestoreToCount(layer);
            canvas.RestoreToCount(clip);
        }
    }
}
