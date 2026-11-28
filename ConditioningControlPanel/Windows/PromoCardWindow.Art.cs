using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel;

/// <summary>The promo card's art: the die-cut gameplay stickers, the game's crayon spiral, the
/// sparkles, the hook's highlighter, and the slow ambient loop that turns and drifts them.</summary>
public partial class PromoCardWindow
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const int MaxStickerBytes = 3 * 1024 * 1024;
    private const double SpinSecondsPerTurn = 140;
    private const double DriftSeconds = 6;

    private readonly FrameClock _ambient = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _ambientWatch = new();
    private double _ambientSeconds;

    partial void BuildArt(IReadOnlyList<ImageSource?>? stickers)
    {
        SpiralImage.Source = Spiral.Value;
        Canvas.SetLeft(SpiralImage, 287 - 543);
        Canvas.SetTop(SpiralImage, 168 - 543);
        SpiralImage.CacheMode = new BitmapCache(1.5);
        Sparkle(530, 64, 18, Gold); Sparkle(24, 548, 13, Violet); Sparkle(546, 470, 12, Pink);

        // The hook, its last word under the highlighter stroke.
        var hook = _promo.Hook ?? "";
        var cut = hook.TrimEnd().LastIndexOf(' ');
        HookText.Inlines.Clear();
        HookText.Inlines.Add(new Run(cut > 0 ? hook[..(cut + 1)] : ""));
        HookText.Inlines.Add(new Run(cut > 0 ? hook[(cut + 1)..] : hook) { Background = Highlighter });

        HeroBox.Visibility = _promo.Stickers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeroLayout();
        if (stickers != null) PlaceStickers(stickers);
        else if (_promo.Stickers.Count > 0) _ = LoadStickersAsync();
        _ambient.Tick += (_, _) => StepAmbient();
    }

    partial void StartArt()
    {
        MotionFx.AmbientChanged += ApplyAmbient;
        ApplyAmbient();
    }

    partial void StopArt()
    {
        _ambient.Stop();
        MotionFx.AmbientChanged -= ApplyAmbient;
    }

    // ---- ambient: the spiral turns, the stickers drift ----

    /// <summary>Runs the loop only while ambient motion is allowed (Full motion, a tier that permits
    /// it, the panel not standing down for the show, Gentle visuals off); otherwise holds still where
    /// it is. Re-read on every <see cref="MotionFx.AmbientChanged"/>.</summary>
    private void ApplyAmbient()
    {
        if (!_leaving && MotionFx.AllowAmbientLoops && !GentleVisuals.On)
        {
            if (_ambient.IsEnabled) return;
            _ambientWatch.Restart();
            _ambient.Start();
        }
        else
        {
            _ambient.Stop();
            _ambientWatch.Stop();
        }
    }

    private void StepAmbient()
    {
        _ambientSeconds += _ambientWatch.Elapsed.TotalSeconds;
        _ambientWatch.Restart();
        SpiralTurn.Angle = _ambientSeconds * 360 / SpinSecondsPerTurn % 360;
        var phase = Math.Cos(_ambientSeconds * 2 * Math.PI / DriftSeconds);
        HeroDriftTurn.Angle = -0.6 * phase;
        HeroDriftShift.Y = -1 + phase;
    }

    // ---- stickers ----

    /// <summary>Where each sticker sits in the 300 x 200 box for one, two or three pictures:
    /// left, top, width, height, tilt (the cards' layouts at one and a half size).</summary>
    private static readonly double[][][] StickerLayouts =
    {
        new[] { new double[] { 12, 14, 272, 168, -3 } },
        new[] { new double[] { 2, 6, 266, 158, -3 }, new double[] { 158, 118, 134, 80, 5 } },
        new[] { new double[] { 0, 4, 182, 110, -6 }, new double[] { 114, 26, 180, 108, 5 }, new double[] { 40, 94, 192, 110, -2 } },
    };

    private void PlaceStickers(IReadOnlyList<ImageSource?> images)
    {
        HeroBox.Children.Clear();
        var shown = images.Where(i => i != null).Take(3).ToList();
        HeroBox.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeroLayout();
        if (shown.Count == 0) return;
        var layout = StickerLayouts[shown.Count - 1];
        for (var i = 0; i < shown.Count; i++)
        {
            var (x, y, w, h, tilt) = (layout[i][0], layout[i][1], layout[i][2], layout[i][3], layout[i][4]);
            var sticker = new Grid { Width = w, Height = h, RenderTransformOrigin = new Point(0.5, 0.5) };
            sticker.Children.Add(new Border { CornerRadius = new CornerRadius(13), Background = HardShadow, RenderTransform = new TranslateTransform(3, 5) });
            sticker.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(13), Background = Brushes.White, Padding = new Thickness(4),
                Child = new Border { CornerRadius = new CornerRadius(9), Background = new ImageBrush(shown[i]) { Stretch = Stretch.UniformToFill } },
            });
            Tilted(sticker, tilt, 0, 0);
            Canvas.SetLeft(sticker, x);
            Canvas.SetTop(sticker, y);
            HeroBox.Children.Add(sticker);
            if (IsLoaded && Moves) PopIn(sticker, 70 * i, 0.7);
        }
    }

    /// <summary>Fetches the stickers the way AnnouncementPopup fetches its image (10 s, decoded in
    /// memory, no disk cache), with a size cap; the ones that arrive are placed together.</summary>
    private async Task LoadStickersAsync()
    {
        var loads = _promo.Stickers.Select(LoadOneAsync).ToList();
        var images = await Task.WhenAll(loads);
        try { await Dispatcher.InvokeAsync(() => { if (!_leaving) PlaceStickers(images); }); }
        catch (Exception ex) { App.Logger?.Debug("Promo stickers: {E}", ex.Message); }
    }

    private static async Task<ImageSource?> LoadOneAsync(Uri url)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxStickerBytes) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > MaxStickerBytes) return null;
            var bmp = new BitmapImage();
            using (var stream = new System.IO.MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 640;
                bmp.StreamSource = stream;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Promo sticker failed to load: {Error}", ex.Message);
            return null;
        }
    }

    // ---- paper, ink, spiral ----

    /// <summary>The pink highlighter under the hook's last word: a band across the lower half.</summary>
    private static readonly Brush Highlighter = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Colors.Transparent, 0), new(Colors.Transparent, 0.5), new(Color.FromArgb(0xA8, 0xFF, 0x69, 0xB4), 0.5),
        new(Color.FromArgb(0xA8, 0xFF, 0x69, 0xB4), 0.88), new(Colors.Transparent, 0.88),
    }, new Point(0, 0), new Point(0, 1)));

    private void Sparkle(double x, double y, double size, Color color)
    {
        var star = new Path
        {
            Data = Geometry.Parse("M 50,0 L 63,37 100,50 63,63 50,100 37,63 0,50 37,37 Z"), Stretch = Stretch.Fill,
            Width = size, Height = size, Fill = Frozen(new SolidColorBrush(color)),
        };
        Canvas.SetLeft(star, x);
        Canvas.SetTop(star, y);
        SparkleLayer.Children.Add(star);
    }

    /// <summary>The game's spiral (ui/main-menu.js drawSpiral, same numbers): two crayon arms, pink
    /// and violet, each one firm stroke and two loose wobbling ones. Built once, frozen, shared.</summary>
    private static readonly Lazy<ImageSource> Spiral = new(() =>
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(-470, -470, 940, 940))));
        foreach (var (hex, arm) in new[] { (Color.FromRgb(0xFF, 0x69, 0xB4), 0), (Color.FromRgb(0x7B, 0x5C, 0xFF), 1) })
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
                        if (first) { ctx.BeginFigure(p, false, false); first = false; }
                        else ctx.LineTo(p, true, true);
                    }
                }
                geo.Freeze();
                var ink = new SolidColorBrush(Color.FromArgb((byte)(pass > 0 ? 102 : 217), hex.R, hex.G, hex.B));
                var pen = new Pen(ink, pass > 0 ? 15 - pass * 3 : 11) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                group.Children.Add(new GeometryDrawing(null, pen, geo));
            }
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    });
}
