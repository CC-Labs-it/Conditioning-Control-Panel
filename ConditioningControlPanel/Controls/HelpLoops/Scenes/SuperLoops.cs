using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using static ConditioningControlPanel.Controls.HelpLoops.LoopMath;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    // The V2 ("Super") help loops, one per effect. Ids are "Super" + the SuperEffect name, which
    // is what Services/Super/SuperDoor.HelpId hands the popover.

    internal static class SuperLoopKit
    {
        public static IReadOnlyList<HelpLoopStep> Steps(string key, double a, double b, double c, double d) => new[]
        {
            new HelpLoopStep(key + "_1", a, b),
            new HelpLoopStep(key + "_2", b, c),
            new HelpLoopStep(key + "_3", c, d),
        };

        /// <summary>Little drops flying out of a pop. k 0..1.</summary>
        public static void Drops(LoopFrame f, double x, double y, double k, double reach, Brush brush)
        {
            if (k <= 0 || k >= 1) return;
            using (f.Fade(f.Front, 1 - k))
                for (int m = 0; m < 6; m++)
                {
                    double a = m / 6.0 * Math.PI * 2, d = reach * EaseOut(k);
                    f.Front.DrawEllipse(brush, null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d), 3, 3);
                }
        }

        /// <summary>A ring that grows and fades. k 0..1.</summary>
        public static void Ring(LoopFrame f, double x, double y, double k, double from, double to, Brush brush)
        {
            if (k <= 0 || k >= 1) return;
            double r = Lerp(from, to, EaseOut(k));
            using (f.Fade(f.Front, 1 - k))
                f.Front.DrawEllipse(null, new Pen(brush, 2.5), new Point(x, y), r, r);
        }
    }

    /// <summary>Deck: a click flips a flash, the next cracks it, the third breaks it and knocks the others over.</summary>
    internal sealed class SuperDeckLoop : HelpLoopScene
    {
        private static readonly Rect[] R = { new(40, 70, 110, 84), new(185, 60, 116, 96), new(332, 74, 108, 82) };
        private static readonly (double, double, double)[] CursorPath =
            { (700, 430, 236), (1500, 243, 108), (4500, 243, 108), (5600, 430, 230) };

        public override string Id => "SuperFlickerDeck";
        public override double DurationMs => 6300;
        public override double StillMs => 3300;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superdeck", 900, 2600, 4100, 5700);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            double inK = Seg(t, 200, 600), brk = Seg(t, 4300, 4650);

            for (int i = 0; i < 3; i += 2)
            {
                double knock = Seg(t, 4450 + i * 40, 5350);
                double o = inK * (1 - knock);
                if (o <= 0.001) continue;
                double cx = R[i].X + R[i].Width / 2, cy = R[i].Y + R[i].Height / 2;
                using (f.Offset(dc, (i == 0 ? -1 : 1) * 26 * EaseOut(knock), 70 * knock * knock))
                using (f.At(dc, cx, cy, 1, o, (i == 0 ? -1 : 1) * 38 * EaseOut(knock)))
                    f.Photo(dc, R[i], i == 0 ? PhotoLook.Pink : PhotoLook.Sea);
            }

            var m = R[1];
            double mx = m.X + m.Width / 2, my = m.Y + m.Height / 2;
            if (brk < 1 && inK > 0)
            {
                double flip = Seg(t, 1500, 1900);
                double sx = Math.Max(.04, Math.Abs(Math.Cos(flip * Math.PI)));
                using (f.Fade(dc, inK * (1 - brk)))
                {
                    dc.PushTransform(new ScaleTransform(sx * (1 + .12 * brk), 1 + .12 * brk, mx, my));
                    f.Photo(dc, m, flip < .5 ? PhotoLook.Stripes : PhotoLook.Pink);
                    double crack = Seg(t, 2900, 3020);
                    if (crack > 0)
                        using (f.Fade(dc, crack))
                        {
                            var pen = new Pen(f.P.White, 1.6);
                            dc.DrawLine(pen, new Point(mx - 8, m.Y), new Point(mx + 4, my - 10));
                            dc.DrawLine(pen, new Point(mx + 4, my - 10), new Point(mx - 14, my + 12));
                            dc.DrawLine(pen, new Point(mx - 14, my + 12), new Point(mx - 2, m.Bottom));
                            dc.DrawLine(pen, new Point(mx + 4, my - 10), new Point(m.Right, my + 6));
                        }
                    dc.Pop();
                }
            }

            double fly = Seg(t, 4300, 5200);
            if (fly > 0 && fly < 1)
                using (f.Fade(dc, 1 - fly))
                    for (int s = 0; s < 7; s++)
                    {
                        double a = s / 7.0 * Math.PI * 2 + .4, d = 95 * EaseOut(fly);
                        double x = mx + Math.Cos(a) * d, y = my + Math.Sin(a) * d + 40 * fly * fly;
                        using (f.At(dc, x, y, 1, 1, 200 * fly + s * 40))
                            dc.DrawRectangle(s % 2 == 0 ? f.P.Accent : f.P.Lilac, null, new Rect(x - 9, y - 6, 18, 12));
                    }
            SuperLoopKit.Ring(f, mx, my, Seg(t, 4300, 5000), 20, 170, f.P.Gold);

            var c = Path(CursorPath, t);
            foreach (var click in new[] { 1500.0, 2900, 4300 }) f.Ripple(243, 108, Seg(t, click + 20, click + 420));
            f.Cursor(c.X, c.Y, (t > 1480 && t < 1620) || (t > 2880 && t < 3020) || (t > 4280 && t < 4420));
        }
    }

    /// <summary>Froth: bubbles merge, a big one turns gold, popping it takes the neighbours along.</summary>
    internal sealed class SuperFrothLoop : HelpLoopScene
    {
        private static readonly Point[] Small = { new(92, 78), new(366, 66), new(392, 186), new(96, 196) };
        private static readonly Point Big = new(222, 138);
        private static readonly Brush Droplet = LoopPalette.Solid("#ffc2e2");
        private static readonly (double, double, double)[] CursorPath =
            { (4000, 440, 236), (5000, 222, 138), (5700, 222, 138), (6500, 440, 236) };

        public override string Id => "SuperInnerBloom";
        public override double DurationMs => 7000;
        public override double StillMs => 4200;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superfroth", 500, 3300, 4900, 6500);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            double inK = Seg(t, 150, 600);
            const double Pop = 5000;

            for (int i = 0; i < Small.Length; i++)
            {
                double dist = (Small[i] - Big).Length, at = Pop + 120 + dist * 1.6;
                if (t < at) f.Bubble(Small[i].X + 5 * Math.Sin(t / 500 + i), Small[i].Y + 4 * Math.Cos(t / 640 + i), .8, inK);
                SuperLoopKit.Drops(f, Small[i].X, Small[i].Y, Seg(t, at, at + 450), 22, Droplet);
            }

            double m1 = EaseInOut(Seg(t, 700, 1800)), m2 = EaseInOut(Seg(t, 2300, 3400));
            if (t < Pop)
            {
                if (m1 < 1)
                {
                    f.Bubble(Lerp(150, 205, m1), Lerp(160, 142, m1), 1, inK);
                    f.Bubble(Lerp(262, 215, m1), Lerp(118, 138, m1), 1, inK);
                }
                else
                {
                    double grow = Seg(t, 1800, 2200), grow2 = Seg(t, 3400, 3800);
                    double wob = 1 + .1 * Math.Sin((t - 1800) / 60) * (1 - grow) + .1 * Math.Sin((t - 3400) / 60) * (grow2 > 0 ? 1 - grow2 : 0);
                    double sc = (m2 < 1 ? Lerp(1.15, 1.45, EaseOut(grow)) : Lerp(1.45, 1.9, EaseOut(grow2))) * wob;
                    double x = Lerp(210, Big.X, m2), y = Lerp(140, Big.Y, m2);
                    f.Bubble(x, y, sc);
                    if (m2 < 1) f.Bubble(Lerp(344, 250, m2), Lerp(190, 150, m2), 1);
                    else
                        using (f.Fade(dc, grow2))
                            dc.DrawEllipse(null, new Pen(f.P.Gold, 3), new Point(x, y), 22 * sc, 22 * sc);
                }
            }
            SuperLoopKit.Drops(f, Big.X, Big.Y, Seg(t, Pop, Pop + 500), 46, f.P.Gold);
            SuperLoopKit.Ring(f, Big.X, Big.Y, Seg(t, Pop, Pop + 800), 40, 210, f.P.Gold);
            f.Floater(Big.X - 14, Big.Y - 30, Seg(t, Pop, Pop + 1500), "+1 +1 +1", f.P.Gold);

            var c = Path(CursorPath, t);
            f.Cursor(c.X, c.Y, t > Pop - 40 && t < Pop + 110);
        }
    }

    /// <summary>Whirlpool: grows under a resting mouse, pulls things into its eye, shrinks when the mouse moves.</summary>
    internal sealed class SuperWhirlpoolLoop : HelpLoopScene
    {
        private static readonly (double, double, double)[] CursorPath =
            { (0, 240, 128), (4700, 240, 128), (5700, 110, 190), (6500, 330, 80), (7400, 240, 128) };
        private static readonly Brush Droplet = LoopPalette.Solid("#ffc2e2");

        public override string Id => "SuperVortex";
        public override double DurationMs => 7600;
        public override double StillMs => 3000;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superwhirlpool", 300, 2000, 4600, 7200);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            var c = Path(CursorPath, t);
            double g = EaseInOut(Seg(t, 300, 3300)) * (1 - EaseInOut(Seg(t, 4700, 6300)));
            double radius = 12 + 92 * g;

            var fade = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute, Center = c, GradientOrigin = c, RadiusX = radius, RadiusY = radius,
            };
            fade.GradientStops.Add(new GradientStop(f.P.AccentColor, 0));
            fade.GradientStops.Add(new GradientStop(f.P.AccentColor, .45));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, f.P.AccentColor.R, f.P.AccentColor.G, f.P.AccentColor.B), 1));
            var pen = new Pen(fade, 3 + 4 * g) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round };
            for (int arm = 0; arm < 3; arm++)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                    for (int i = 0; i <= 96; i++)
                    {
                        double u = i / 96.0;
                        double a = t * .0042 + arm * Math.PI * 2 / 3 + u * Math.PI * 2 * (.9 + 1.2 * g);
                        var p = new Point(c.X + Math.Cos(a) * radius * u, c.Y + Math.Sin(a) * radius * u);
                        if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                    }
                using (f.Fade(dc, .85)) dc.DrawGeometry(null, pen, geo);
            }

            // A picture, then a bubble, spiral into the eye and pop there.
            Eat(f, t, 1900, 3700, new Point(96, 70), true, c);
            Eat(f, t, 2700, 4400, new Point(398, 196), false, c);

            f.Cursor(c.X, c.Y, false);
        }

        private static void Eat(LoopFrame f, double t, double from, double to, Point start, bool picture, Point eye)
        {
            double k = Seg(t, from, to), e = EaseInOut(k), inK = Seg(t, 250, 650);
            if (k < 1)
            {
                var d = start - eye;
                double a = Math.Atan2(d.Y, d.X) + e * 5.2, dist = d.Length * (1 - e);
                double x = eye.X + Math.Cos(a) * dist, y = eye.Y + Math.Sin(a) * dist, sc = 1 - .8 * e;
                if (picture)
                    using (f.At(f.Front, x, y, sc, inK, e * 300))
                        f.Photo(f.Front, new Rect(x - 38, y - 27, 76, 54), PhotoLook.Stripes);
                else f.Bubble(x, y, sc, inK);
            }
            SuperLoopKit.Drops(f, eye.X, eye.Y, Seg(t, to, to + 450), 30, picture ? f.P.Lilac : Droplet);
        }
    }

    /// <summary>Steam: pink fog rolls in, the mouse wipes it, it creeps back with words in it.</summary>
    internal sealed class SuperSteamLoop : HelpLoopScene
    {
        private static readonly (double, double, double)[] CursorPath =
        {
            (2200, 440, 236), (2900, 110, 80), (3500, 330, 110), (4100, 130, 150), (4700, 350, 180), (5600, 440, 236),
        };
        private static readonly Brush Fog = LoopPalette.Freeze(new RadialGradientBrush(
            LoopPalette.Css("#ff7fc4"), LoopPalette.Css("#ff7fc400")));

        public override string Id => "SuperCreep";
        public override double DurationMs => 7800;
        public override double StillMs => 3800;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_supersteam", 300, 2600, 4900, 7200);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            double level = Seg(t, 300, 2300) * (1 - Seg(t, 7100, 7700));
            if (level > 0)
                for (int gy = 0; gy < 7; gy++)
                    for (int gx = 0; gx < 11; gx++)
                    {
                        double x = gx * 48 + 10 * Math.Sin(t / 900 + gy * 1.7 + gx);
                        double y = gy * 45 + 8 * Math.Cos(t / 1100 + gx * 1.3);
                        // The fog comes in from the edges: the middle fills last.
                        double edge = Math.Min(Math.Min(x, 480 - x) / 240, Math.Min(y, 270 - y) / 135);
                        double here = Clamp(level * 2.2 - edge * 1.2);
                        double clear = 0;
                        for (int s = 0; s < 12; s++)
                        {
                            double age = s * 220, at = t - age;
                            if (at < 2800 || at > 4800) continue;
                            var p = Path(CursorPath, at);
                            double near = Clamp(1 - Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) / 62);
                            clear = Math.Max(clear, near * (1 - age / 2640));
                        }
                        double o = here * (1 - clear) * .5;
                        if (o <= 0.01) continue;
                        dc.PushOpacity(o);
                        dc.DrawEllipse(Fog, null, new Point(x, y), 46, 46);
                        dc.Pop();
                    }

            double words = Seg(t, 5200, 6100) * (1 - Seg(t, 7000, 7600));
            f.Text("deeper", 150, 86, 24, f.P.White, bold: true, align: TextAlignment.Center, opacity: words * .7);
            f.Text("let go", 330, 150, 20, f.P.White, bold: true, align: TextAlignment.Center, opacity: words * .55);

            var c = Path(CursorPath, t);
            f.Cursor(c.X, c.Y, false);
        }
    }

    /// <summary>Afterglow: every flashed word leaves a ghost; four ghosts make a line that lights up and sinks.</summary>
    internal sealed class SuperAfterglowLoop : HelpLoopScene
    {
        private static readonly string[] Words = { "sink", "deeper", "and", "drift" };
        private const double LineY = 196, Finale = 5000;

        public override string Id => "SuperAfterglow";
        public override double DurationMs => 7400;
        public override double StillMs => 4300;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superafterglow", 500, 2800, 4900, 6900);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            double total = -10;
            var widths = new double[Words.Length];
            for (int i = 0; i < Words.Length; i++) { widths[i] = f.Measure(Words[i], 16, bold: true).Width; total += widths[i] + 10; }

            double lit = Seg(t, Finale, Finale + 500), sink = Seg(t, Finale + 1100, Finale + 1900);
            double x = 240 - total / 2;
            for (int i = 0; i < Words.Length; i++)
            {
                double at = 600 + i * 1100, slot = x + widths[i] / 2;
                x += widths[i] + 10;
                double flash = Seg(t, at, at + 140) * (1 - Seg(t, at + 420, at + 560));
                f.Text(Words[i], 240, 92, 30, f.P.White, bold: true, align: TextAlignment.Center, opacity: flash);

                double go = Seg(t, at + 420, at + 1100);
                if (go <= 0 || sink >= 1) continue;
                double e = EaseInOut(go);
                double size = Lerp(30, 16, e), gx = Lerp(240, slot, e), gy = Lerp(92, LineY, e) + 26 * sink * sink;
                var brush = lit > 0 ? f.P.Gold : f.P.Accent;
                f.Text(Words[i], gx, gy, size * (1 + .08 * lit * (1 - sink)), brush, bold: true, align: TextAlignment.Center,
                    opacity: Lerp(.45, 1, lit) * (1 - sink));
            }
            if (lit > 0 && sink < 1)
                using (f.Fade(f.Front, lit * (1 - sink) * .8))
                    f.Front.DrawRectangle(f.P.Gold, null, new Rect(240 - total / 2, LineY + 24 + 26 * sink * sink, total, 2));
        }
    }

    /// <summary>Scrawl: the bouncing word stamps the walls it hits; a click slams it across the screen.</summary>
    internal sealed class SuperScrawlLoop : HelpLoopScene
    {
        private const string Word = "DEEPER";
        private static readonly (double T, double X, double Y)[] Bounce =
        {
            (0, 110, 70), (1200, 428, 150), (2300, 250, 226), (3400, 52, 120), (4300, 190, 22), (5000, 300, 110),
            (5220, 428, 196), (6400, 250, 60), (7400, 110, 70),
        };
        // Wall hits: time, where the stamp lands, how it is turned.
        private static readonly (double T, double X, double Y, double Rot)[] Stamps =
        {
            (1200, 468, 150, 90), (2300, 250, 236, 0), (3400, 12, 120, -90), (4300, 190, 6, 0), (5220, 468, 196, 90),
        };

        public override string Id => "SuperScrawl";
        public override double DurationMs => 7400;
        public override double StillMs => 3600;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superscrawl", 300, 2400, 4900, 6800);

        private static Point At(double t)
        {
            for (int i = 1; i < Bounce.Length; i++)
                if (t <= Bounce[i].T)
                {
                    double k = Seg(t, Bounce[i - 1].T, Bounce[i].T);
                    return new Point(Lerp(Bounce[i - 1].X, Bounce[i].X, k), Lerp(Bounce[i - 1].Y, Bounce[i].Y, k));
                }
            return new Point(Bounce[^1].X, Bounce[^1].Y);
        }

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            double gone = Seg(t, 6700, 7200);
            foreach (var s in Stamps)
            {
                double k = Seg(t, s.T, s.T + 220);
                if (k <= 0) continue;
                using (f.At(dc, s.X, s.Y + 6, Lerp(1.8, 1, EaseOut(k)), k * .6 * (1 - gone), s.Rot))
                    f.DrawText(dc, Word, s.X, s.Y, 9, f.P.Accent, LoopFrame.Display, FontWeights.SemiBold, TextAlignment.Center);
            }

            bool slam = t > 5000 && t < 5220;
            if (slam)
                for (int g = 3; g >= 1; g--)
                {
                    var q = At(t - g * 26);
                    f.Text(Word, q.X, q.Y - 11, 20, f.P.Accent, bold: true, align: TextAlignment.Center, opacity: .22);
                }
            var p = At(t);
            f.Text(Word, p.X, p.Y - 11, 20, slam ? f.P.White : f.P.Accent, bold: true, align: TextAlignment.Center);
            SuperLoopKit.Ring(f, 440, 196, Seg(t, 5220, 5700), 10, 70, f.P.Accent);

            var c = Path(new (double, double, double)[] { (3900, 440, 236), (5000, 300, 116), (5300, 300, 116), (6300, 440, 236) }, t);
            f.Ripple(300, 112, Seg(t, 5010, 5400));
            f.Cursor(c.X, c.Y, t > 4980 && t < 5120);
        }
    }

    /// <summary>Lights Down: the room falls away while you watch; look away and the lights stutter back.</summary>
    internal sealed class SuperLightsDownLoop : HelpLoopScene
    {
        private static readonly Rect Video = new(150, 56, 180, 104);
        private static readonly (double T, double V)[] Dark =
        {
            (0, 0), (900, 0), (4300, .86), (4600, .86), (4700, .3), (4800, .7), (4950, .2), (5100, .55),
            (5300, .1), (5500, .3), (5700, 0), (7200, 0),
        };
        private static readonly Brush Black = LoopPalette.Solid("#05030b");

        public override string Id => "SuperLightsDown";
        public override double DurationMs => 7200;
        public override double StillMs => 3600;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superlightsdown", 200, 1200, 4500, 6600);

        public override void Draw(LoopFrame f, double t)
        {
            f.Desktop();
            var dc = f.Front;
            f.Photo(dc, Video, PhotoLook.Sea);
            var play = new StreamGeometry();
            using (var ctx = play.Open())
            {
                ctx.BeginFigure(new Point(232, 94), true, true);
                ctx.LineTo(new Point(254, 108), true, false);
                ctx.LineTo(new Point(232, 122), true, false);
            }
            using (f.Fade(dc, .8)) dc.DrawGeometry(f.P.White, null, play);

            double d = Schedule(Dark, t);
            if (d > 0.005)
            {
                var room = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight)),
                    new RectangleGeometry(Video, 6, 6));
                using (f.Fade(dc, d)) dc.DrawGeometry(Black, null, room);
            }

            double away = EaseInOut(Seg(t, 4350, 4650)) * (1 - EaseInOut(Seg(t, 6300, 6700)));
            f.Eye(240, 214, 1, away, -(1 - away), 1.25);
        }
    }

    /// <summary>Undertow: a clear lens in the blur shrinks on its own; a click widens it again.</summary>
    internal sealed class SuperUndertowLoop : HelpLoopScene
    {
        private static readonly Point Centre = new(220, 124);
        private static readonly (double T, double V)[] Lens = { (0, 84), (4400, 12), (5000, 12), (5450, 88), (7400, 84) };
        private static readonly (double T, double V)[] Blur = { (0, 4.5), (4400, 8), (5000, 8), (5450, 4.5), (7400, 4.5) };
        private static readonly (double, double, double)[] CursorPath =
            { (3600, 440, 236), (4800, 220, 124), (5500, 220, 124), (6600, 440, 236) };
        private static readonly Pen Rim = LoopPalette.Freeze(new Pen(LoopPalette.Solid("#ffffff55"), 1.5));

        public override string Id => "SuperUndertow";
        public override double DurationMs => 7400;
        public override double StillMs => 2200;
        public override IReadOnlyList<HelpLoopStep> Steps { get; } = SuperLoopKit.Steps("help_loop_superundertow", 300, 1500, 4900, 6800);

        public override void Draw(LoopFrame f, double t)
        {
            f.BackBlur = Schedule(Blur, t);
            f.Desktop();
            var dc = f.Front;
            double r = Schedule(Lens, t);
            using (f.ClipTo(dc, new EllipseGeometry(Centre, r, r)))
            {
                dc.DrawRectangle(f.P.Ground, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
                f.Desktop(null, true, dc);
            }
            dc.DrawEllipse(null, Rim, Centre, r, r);

            var c = Path(CursorPath, t);
            f.Ripple(Centre.X, Centre.Y, Seg(t, 5010, 5420));
            f.Cursor(c.X, c.Y, t > 4980 && t < 5130);
        }
    }
}
