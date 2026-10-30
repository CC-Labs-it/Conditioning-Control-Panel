using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    public static class LanguageFlag
    {
        public const double FlagWidth = 20;
        public const double FlagHeight = 14;
        private const double Radius = 2;

        private static readonly object Gate = new();
        private static readonly Dictionary<string, IImage> Cache = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, Action<DrawingContext>> Painters = new(StringComparer.Ordinal)
        {
            ["en"] = UnitedKingdom,
            ["zh"] = China,
            ["ja"] = Japan,
            ["ko"] = Korea,
            ["es"] = Spain,
            ["pt"] = Brazil,
            ["fr"] = dc => VBands(dc, Hex("#002654"), Hex("#FFFFFF"), Hex("#CE1126")),
            ["it"] = dc => VBands(dc, Hex("#009246"), Hex("#FFFFFF"), Hex("#CE2B37")),
            ["de"] = dc => HBands(dc, Hex("#000000"), Hex("#DD0000"), Hex("#FFCE00")),
            ["ru"] = dc => HBands(dc, Hex("#FFFFFF"), Hex("#0039A6"), Hex("#D52B1E")),
            ["nl"] = dc => HBands(dc, Hex("#AE1C28"), Hex("#FFFFFF"), Hex("#21468B")),
            ["pl"] = dc => HBands(dc, Hex("#FFFFFF"), Hex("#DC143C")),
        };

        public static bool Has(string? code) => Key(code) != null;

        public static IImage? For(string? code)
        {
            var key = Key(code);
            if (key == null) return null;
            lock (Gate)
            {
                if (!Cache.TryGetValue(key, out var image))
                {
                    image = new FlagImage(key);
                    Cache[key] = image;
                }
                return image;
            }
        }

        public static void Dress(ComboBox combo, ComboBoxItem item, string code, string name)
        {
            item.Content = new LanguageOption(code, name, For(code));
            item.ContentTemplate = new FuncDataTemplate<LanguageOption>((option, _) =>
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new Image { Source = option!.Flag, Width = FlagWidth, Height = FlagHeight,
                    Stretch = Stretch.Fill, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                panel.Children.Add(new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis });
                return panel;
            });
            combo.SelectionBoxItemTemplate = item.ContentTemplate;
            TextSearch.SetText(item, name);
            AutomationProperties.SetName(item, name);
        }

        private static string? Key(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var c = code.Trim().ToLowerInvariant();
            if (Painters.ContainsKey(c)) return c;
            var dash = c.IndexOf('-');
            if (dash > 0 && Painters.ContainsKey(c[..dash])) return c[..dash];
            return null;
        }

        // Draw on the live context: DrawingGroup.Open cannot record rounded clips in Avalonia.
        private sealed class FlagImage(string key) : IImage
        {
            public Size Size => new(FlagWidth, FlagHeight);

            public void Draw(DrawingContext dc, Rect sourceRect, Rect destRect)
            {
                if (sourceRect.Width <= 0 || sourceRect.Height <= 0) return;
                var map = Matrix.CreateTranslation(-sourceRect.X, -sourceRect.Y)
                    * Matrix.CreateScale(destRect.Width / sourceRect.Width, destRect.Height / sourceRect.Height)
                    * Matrix.CreateTranslation(destRect.X, destRect.Y);
                using (dc.PushClip(destRect))
                using (dc.PushTransform(map))
                {
                    var full = new Rect(Size);
                    using (dc.PushClip(new RoundedRect(full, Radius))) Painters[key](dc);
                    dc.DrawRectangle(null, Outline,
                        new Rect(0.5, 0.5, FlagWidth - 1, FlagHeight - 1), Radius - 0.5, Radius - 0.5);
                }
            }
        }

        private static readonly Pen Outline = new Pen(Hex("#73000000"), 1);

        private static void UnitedKingdom(DrawingContext dc)
        {
            var navy = Hex("#012169");
            var red = Hex("#C8102E");
            var white = Hex("#FFFFFF");
            dc.DrawRectangle(navy, null, new Rect(0, 0, FlagWidth, FlagHeight));

            var a1 = new Point(-2, -1.4); var a2 = new Point(FlagWidth + 2, FlagHeight + 1.4);
            var b1 = new Point(FlagWidth + 2, -1.4); var b2 = new Point(-2, FlagHeight + 1.4);
            var whiteDiag = new Pen(white, 3);
            var redDiag = new Pen(red, 1);
            dc.DrawLine(whiteDiag, a1, a2); dc.DrawLine(whiteDiag, b1, b2);
            dc.DrawLine(redDiag, a1, a2); dc.DrawLine(redDiag, b1, b2);

            dc.DrawRectangle(white, null, new Rect(FlagWidth / 2 - 2.2, 0, 4.4, FlagHeight));
            dc.DrawRectangle(white, null, new Rect(0, FlagHeight / 2 - 2.2, FlagWidth, 4.4));
            dc.DrawRectangle(red, null, new Rect(FlagWidth / 2 - 1.3, 0, 2.6, FlagHeight));
            dc.DrawRectangle(red, null, new Rect(0, FlagHeight / 2 - 1.3, FlagWidth, 2.6));
        }

        private static void China(DrawingContext dc)
        {
            dc.DrawRectangle(Hex("#DE2910"), null, new Rect(0, 0, FlagWidth, FlagHeight));
            dc.DrawGeometry(Hex("#FFDE00"), null, Star(new Point(5, 4.6), 3.1, 1.2));
        }

        private static void Japan(DrawingContext dc)
        {
            dc.DrawRectangle(Hex("#FFFFFF"), null, new Rect(0, 0, FlagWidth, FlagHeight));
            dc.DrawEllipse(Hex("#BC002D"), null, new Point(FlagWidth / 2, FlagHeight / 2), 4.2, 4.2);
        }

        private static void Korea(DrawingContext dc)
        {
            var black = Hex("#000000");
            var red = Hex("#CD2E3A");
            var blue = Hex("#0047A0");
            dc.DrawRectangle(Hex("#FFFFFF"), null, new Rect(0, 0, FlagWidth, FlagHeight));

            const double cx = FlagWidth / 2, cy = FlagHeight / 2, r = 3.5;
            var tilt = Math.Atan2(FlagHeight, FlagWidth) * 180 / Math.PI;
            using (dc.PushTransform(new RotateTransform(tilt, cx, cy).Value))
            {
                dc.DrawEllipse(red, null, new Point(cx, cy), r, r);
                dc.DrawGeometry(blue, null, Geometry.Parse("M 6.5,7 A 3.5,3.5 0 0 0 13.5,7 Z"));
                dc.DrawEllipse(red, null, new Point(cx - r / 2, cy), r / 2, r / 2);
                dc.DrawEllipse(blue, null, new Point(cx + r / 2, cy), r / 2, r / 2);
            }

            Trigram(dc, black, new Point(3.7, 2.7), new Point(cx, cy), "111");
            Trigram(dc, black, new Point(FlagWidth - 3.7, 2.7), new Point(cx, cy), "010");
            Trigram(dc, black, new Point(3.7, FlagHeight - 2.7), new Point(cx, cy), "101");
            Trigram(dc, black, new Point(FlagWidth - 3.7, FlagHeight - 2.7), new Point(cx, cy), "000");
        }

        private static void Trigram(DrawingContext dc, Brush ink, Point at, Point centre, string bars)
        {
            const double w = 3.2, h = 0.6, gap = 0.45, split = 0.6;
            var angle = Math.Atan2(centre.Y - at.Y, centre.X - at.X) * 180 / Math.PI + 90;
            using (dc.PushTransform(new RotateTransform(angle, at.X, at.Y).Value))
            {
                var top = at.Y - (bars.Length * h + (bars.Length - 1) * gap) / 2;
                for (int i = 0; i < bars.Length; i++)
                {
                    var y = top + i * (h + gap);
                    if (bars[i] == '1')
                        dc.DrawRectangle(ink, null, new Rect(at.X - w / 2, y, w, h));
                    else
                    {
                        var half = (w - split) / 2;
                        dc.DrawRectangle(ink, null, new Rect(at.X - w / 2, y, half, h));
                        dc.DrawRectangle(ink, null, new Rect(at.X + split / 2, y, half, h));
                    }
                }
            }
        }

        private static void Spain(DrawingContext dc)
        {
            dc.DrawRectangle(Hex("#AA151B"), null, new Rect(0, 0, FlagWidth, FlagHeight));
            dc.DrawRectangle(Hex("#F1BF00"), null, new Rect(0, FlagHeight / 4, FlagWidth, FlagHeight / 2));
        }

        private static void Brazil(DrawingContext dc)
        {
            dc.DrawRectangle(Hex("#009C3B"), null, new Rect(0, 0, FlagWidth, FlagHeight));
            var diamond = new StreamGeometry();
            using (var g = diamond.Open())
            {
                g.BeginFigure(new Point(1.8, FlagHeight / 2), true);
                g.LineTo(new Point(FlagWidth / 2, 1.5));
                g.LineTo(new Point(FlagWidth - 1.8, FlagHeight / 2));
                g.LineTo(new Point(FlagWidth / 2, FlagHeight - 1.5));
                g.EndFigure(true);
            }

            dc.DrawGeometry(Hex("#FFDF00"), null, diamond);
            dc.DrawEllipse(Hex("#002776"), null, new Point(FlagWidth / 2, FlagHeight / 2), 3.3, 3.3);
        }

        private static void HBands(DrawingContext dc, params Brush[] bands)
        {
            var h = FlagHeight / bands.Length;
            for (int i = 0; i < bands.Length; i++)
                dc.DrawRectangle(bands[i], null, new Rect(0, i * h, FlagWidth, FlagHeight - i * h));
        }

        private static void VBands(DrawingContext dc, params Brush[] bands)
        {
            var w = FlagWidth / bands.Length;
            for (int i = 0; i < bands.Length; i++)
                dc.DrawRectangle(bands[i], null, new Rect(i * w, 0, FlagWidth - i * w, FlagHeight));
        }

        private static Geometry Star(Point c, double outer, double inner)
        {
            var star = new StreamGeometry();
            using (var g = star.Open())
            {
                for (int i = 0; i < 10; i++)
                {
                    var r = i % 2 == 0 ? outer : inner;
                    var a = (-90 + i * 36) * Math.PI / 180;
                    var p = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                    if (i == 0) g.BeginFigure(p, true);
                    else g.LineTo(p);
                }
                g.EndFigure(true);
            }

            return star;
        }

        private static SolidColorBrush Hex(string hex)
        {
            return new SolidColorBrush(Color.Parse(hex));
        }

    }

    public sealed class LanguageOption
    {
        public LanguageOption(string code, string name, IImage? flag)
        {
            Code = code;
            Name = name;
            Flag = flag;
        }

        public string Code { get; }
        public string Name { get; }
        public IImage? Flag { get; }

        public override string ToString() => Name;
    }
}
