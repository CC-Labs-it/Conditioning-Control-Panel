using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public partial class AmbientFxCanvas
    {
        internal readonly record struct FogSprite(float X, float Y, float Size, float Alpha);
        private static readonly Lazy<SKImage> FogDot = new(() =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(128, 128));
            surface.Canvas.Clear(SKColors.Transparent);
            using var shader = SKShader.CreateRadialGradient(new SKPoint(64, 64), 64,
                new[] { SKColors.White, SKColors.White.WithAlpha(0) }, new[] { 0f, 1f }, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader };
            surface.Canvas.DrawCircle(64, 64, 64, paint);
            return surface.Snapshot();
        });

        private void DrawFogPlus(DrawingContext context, float w, float h, float min, float intensity)
        {
            if (_mistDot is not IImmutableBrush brush) return;
            var sprites = new List<FogSprite>(_puffs.Length);
            foreach (var p in _puffs)
            {
                float a = p.BaseA * (0.70f + 0.30f * (float)Math.Sin(p.Phase + _fogT * p.PhaseSpd)) * intensity * _mistAlpha;
                if (a <= 0.004f) continue;
                sprites.Add(new FogSprite(Frac2(p.X + p.VX * _fogT) * w,
                    Frac2(p.Y + p.VY * _fogT) * h, p.R * min * 2f, a));
            }
            context.Custom(new FogDrawOp(new Rect(0, 0, w, h), sprites.ToArray(), _mistColor, brush));
        }

        internal static void PaintFog(SKCanvas canvas, FogSprite[] sprites, Color color, SKRect bounds)
        {
            canvas.SaveLayer(bounds, null);
            try
            {
                using var filter = SKColorFilter.CreateBlendMode(new SKColor(color.R, color.G, color.B), SKBlendMode.Modulate);
                using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus, ColorFilter = filter };
                foreach (var sprite in sprites)
                {
                    paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(sprite.Alpha * 255f, 0, 255));
                    float r = sprite.Size / 2;
                    canvas.DrawImage(FogDot.Value, new SKRect(sprite.X - r, sprite.Y - r, sprite.X + r, sprite.Y + r), paint);
                }
            }
            finally { canvas.Restore(); }
        }

        private sealed class FogDrawOp : ICustomDrawOperation
        {
            private readonly FogSprite[] _sprites;
            private readonly Color _color;
            private readonly IImmutableBrush _brush;
            public FogDrawOp(Rect bounds, FogSprite[] sprites, Color color, IImmutableBrush brush)
                => (Bounds, _sprites, _color, _brush) = (bounds, sprites, color, brush);
            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }
            public void Render(ImmediateDrawingContext context)
            {
                var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (feature != null)
                {
                    using var lease = feature.Lease();
                    PaintFog(lease.SkCanvas, _sprites, _color, new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
                    return;
                }
                foreach (var sprite in _sprites)
                {
                    using var opacity = context.PushOpacity(Math.Clamp(sprite.Alpha, 0, 1), Bounds);
                    context.DrawEllipse(_brush, null, new Point(sprite.X, sprite.Y), sprite.Size / 2, sprite.Size / 2);
                }
            }
        }
    }
}
