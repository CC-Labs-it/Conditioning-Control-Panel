using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>PORTED from ConditioningControlPanel/Windows/PromoCardWindow.Art.cs (759f40d621): the die-cut stickers,
    /// the game's crayon spiral, the sparkles and the slow ambient loop that turns and drifts them. Deviation: the loop
    /// re-reads AllowAmbientLoops each tick (no MotionFx.AmbientChanged here).</summary>
    public partial class PromoCardWindow
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private const int MaxStickerBytes = 3 * 1024 * 1024;
        private readonly DispatcherTimer _ambient = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch _ambientWatch = new();
        private readonly RotateTransform _spin = new(), _driftTurn = new();
        private readonly TranslateTransform _driftShift = new();
        private double _ambientSeconds;

        partial void StartArt() { _ambientWatch.Restart(); _ambient.Start(); }

        partial void StopArt() => _ambient.Stop();

        partial void BuildArt(IReadOnlyList<IImage?>? stickers)
        {
            _ambient.Tick += (_, _) => StepAmbient();
            SpiralImage.Source = Spiral.Value;
            SpiralImage.RenderTransform = _spin;
            HeroBox.RenderTransform = new TransformGroup { Children = { _driftTurn, _driftShift } };
            Sparkle(530, 64, 18, Gold); Sparkle(24, 548, 13, Violet); Sparkle(546, 470, 12, Pink);

            HeroBox.IsVisible = _promo.Stickers.Count > 0;
            HeroLayout();
            if (stickers != null) PlaceStickers(stickers);
            else if (_promo.Stickers.Count > 0) _ = LoadStickersAsync();
        }

        /// <summary>The spiral turns (one turn per 140 s) and the stickers drift (6 s) only while ambient loops are
        /// allowed and Gentle visuals is off; otherwise they hold still where they are.</summary>
        private void StepAmbient()
        {
            var step = _ambientWatch.Elapsed.TotalSeconds;
            _ambientWatch.Restart();
            if (_leaving || !AmbientFxCanvas.Env.AllowAmbientLoops || GentleVisuals.On) return;
            _ambientSeconds += step;
            _spin.Angle = _ambientSeconds * 360 / 140 % 360;
            var phase = Math.Cos(_ambientSeconds * 2 * Math.PI / 6);
            _driftTurn.Angle = -0.6 * phase;
            _driftShift.Y = -1 + phase;
        }

        /// <summary>Where each sticker sits in the 300 x 200 box for one, two or three pictures: left, top, width,
        /// height, tilt.</summary>
        private static readonly double[][][] StickerLayouts =
        {
            new[] { new double[] { 12, 14, 272, 168, -3 } },
            new[] { new double[] { 2, 6, 266, 158, -3 }, new double[] { 158, 118, 134, 80, 5 } },
            new[] { new double[] { 0, 4, 182, 110, -6 }, new double[] { 114, 26, 180, 108, 5 }, new double[] { 40, 94, 192, 110, -2 } },
        };

        private void PlaceStickers(IReadOnlyList<IImage?> images)
        {
            HeroBox.Children.Clear();
            var shown = images.Where(i => i != null).Take(3).ToList();
            HeroBox.IsVisible = shown.Count > 0;
            HeroLayout();
            for (var i = 0; i < shown.Count; i++)
            {
                var at = StickerLayouts[shown.Count - 1][i];
                var sticker = new Grid { Width = at[2], Height = at[3] };
                sticker.Children.Add(new Border { CornerRadius = new CornerRadius(13), Background = HardShadow, RenderTransform = TransformOperations.Parse("translate(3px, 5px)") });
                sticker.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(13), Background = Brushes.White, Padding = new Thickness(4),
                    Child = new Border { CornerRadius = new CornerRadius(9), ClipToBounds = true, Child = new Image { Source = shown[i], Stretch = Stretch.UniformToFill } },
                });
                Tilted(sticker, at[4], 0, 0);
                Canvas.SetLeft(sticker, at[0]);
                Canvas.SetTop(sticker, at[1]);
                HeroBox.Children.Add(sticker);
                if (IsLoaded && Moves) PopIn(sticker, 70 * i, 0.7);
            }
        }

        /// <summary>Fetches the stickers like AnnouncementPopup's image (10 s, decoded in memory at 640 px, no disk
        /// cache, 3 MB cap); the ones that arrive are placed together.</summary>
        private async Task LoadStickersAsync()
        {
            var images = await Task.WhenAll(_promo.Stickers.Select(LoadOneAsync));
            if (!_leaving) PlaceStickers(images);
        }

        private static async Task<IImage?> LoadOneAsync(Uri url)
        {
            try
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxStickerBytes) return null;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > MaxStickerBytes) return null;
                using var stream = new System.IO.MemoryStream(bytes);
                return Bitmap.DecodeToWidth(stream, 640);
            }
            catch (Exception ex)
            {
                Log.Warning("Promo sticker failed to load: {Error}", ex.Message);
                return null;
            }
        }

        private void Sparkle(double x, double y, double size, Color color)
        {
            var star = new Path
            {
                Data = Geometry.Parse("M 50,0 L 63,37 100,50 63,63 50,100 37,63 0,50 37,37 Z"), Stretch = Stretch.Fill,
                Width = size, Height = size, Fill = new SolidColorBrush(color),
            };
            Canvas.SetLeft(star, x);
            Canvas.SetTop(star, y);
            SparkleLayer.Children.Add(star);
        }

        /// <summary>The game's spiral (ui/main-menu.js drawSpiral, same numbers): two crayon arms, pink and violet,
        /// each one firm stroke and two loose wobbling ones. Built once, shared.</summary>
        private static readonly Lazy<IImage> Spiral = new(() =>
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(-470, -470, 940, 940)) });
            foreach (var (hex, arm) in new[] { (Color.Parse("#FF69B4"), 0), (Violet, 1) })
            {
                for (var pass = 0; pass < 3; pass++)
                {
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        var first = true;
                        for (var t = 0.0; t <= Math.PI * 2 * 5.4; t += 0.04)
                        {
                            var r = 4 + t * 13.2 + (pass > 0 ? Math.Sin(t * 9 + pass * 2) * 2.2 : 0);
                            var a = t + arm * Math.PI;
                            var p = new Point(Math.Cos(a) * r, Math.Sin(a) * r);
                            if (first) { ctx.BeginFigure(p, false); first = false; }
                            else ctx.LineTo(p);
                        }
                        ctx.EndFigure(false);
                    }
                    var ink = new SolidColorBrush(Color.FromArgb((byte)(pass > 0 ? 102 : 217), hex.R, hex.G, hex.B));
                    group.Children.Add(new GeometryDrawing { Geometry = geo, Pen = new Pen(ink, pass > 0 ? 15 - pass * 3 : 11, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round) });
                }
            }
            return new DrawingImage(group);
        });
    }
}
