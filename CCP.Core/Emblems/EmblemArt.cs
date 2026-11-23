namespace ConditioningControlPanel.Emblems;

/// <summary>
/// The emblem drawings, ported 1:1 from the approved mockup's <c>E</c> table
/// (handoff-1124/readable-rows/index.html): 32 x 32 grid, one family, back to front. Every
/// rect, circle, polygon and <c>G(x,y,r,s,..)</c> group is baked into plain absolute path data here,
/// so a head only parses strings. No animation: the mockup's spinning stopwatch hand is a still
/// angle driven by <c>value</c>.
/// </summary>
public static class EmblemArt
{
    /// <summary>
    /// The drawing for <paramref name="kind"/>, back to front. <paramref name="value"/> drives the
    /// parametric ones as the mockup does: Many = item count (1, 2, 3..5 -> 3, 6+ -> 4; below 1 the
    /// mockup's default 3), Most = small rects on the monitor (min(value, 8); below 1 the default 8),
    /// Often = the hand angle as a 0..1 fraction of a full turn (value / the row's maximum).
    /// <paramref name="subject"/> only changes Many and Splits.
    /// </summary>
    public static IReadOnlyList<EmblemLayer> Layers(EmblemKind kind, EmblemSubject subject, double value)
    {
        var k = new Sketch();
        switch (kind)
        {
            case EmblemKind.Often:
                k.Fill(EmblemPath.Rect(14, 1.6, 4, 5, 1), EmblemInk.Main);
                using (k.Push(Affine.RotateAbout(42, 24.7, 7.7))) k.Fill(EmblemPath.Rect(22.8, 6, 3.8, 3.4, 1), EmblemInk.Soft);
                k.Fill(EmblemPath.Circle(16, 18, 11.6), EmblemInk.Main);
                k.Fill(EmblemPath.Circle(16, 18, 8.2), EmblemInk.Cut);
                using (k.Push(Affine.RotateAbout(360 * OftenFraction(value), 16, 18)))
                    k.Stroke("M16 18V12.4", EmblemInk.Main, 2.5, true);
                k.Fill(EmblemPath.Circle(16, 18, 1.7), EmblemInk.Main);
                break;

            case EmblemKind.Many:
                {
                    int n = ManyCount(value);
                    double s = n <= 2 ? 1 : .9;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i - (n - 1) / 2.0;
                        using (k.Push(Affine.Group(16 + t * 4.4, 16.4 - t * 1.1, t * 10, s)))
                            Item(k, subject, back: i < n - 1);
                    }
                    break;
                }

            case EmblemKind.Most:
                Monitor(k);
                for (int i = 0, n = MostCount(value); i < n; i++)
                    k.Fill(EmblemPath.Rect(6.6 + (i % 4) * 5.3, 8.4 + (i / 4) * 6.2, 3.6, 4.6, .8), EmblemInk.Main);
                break;

            case EmblemKind.Random:
                using (k.Push(Affine.RotateAbout(16, 24, 9)))
                {
                    k.Fill(EmblemPath.Rect(18.5, 3.5, 11, 11, 3), EmblemInk.Soft);
                    k.Fill(EmblemPath.Circle(24, 9, 1.5), EmblemInk.Cut);
                }
                using (k.Push(Affine.RotateAbout(-10, 13, 17)))
                {
                    k.Fill(EmblemPath.Rect(3.5, 8, 18.5, 18.5, 4.6), EmblemInk.Main);
                    foreach (var (x, y) in new[] { (8.6, 13.0), (17.0, 13.0), (12.8, 17.2), (8.6, 21.4), (17.0, 21.4) })
                        k.Fill(EmblemPath.Circle(x, y, 1.8), EmblemInk.Cut);
                }
                break;

            case EmblemKind.Click:
                k.Stroke("M6.4 2.6v3.4M2.4 7.4h3.4M3.2 3.8l2.2 2.2", EmblemInk.Soft, 2.2, true);
                k.Fill("M10 8v18.4l4.5-4.3 2.8 6.3 3.3-1.5-2.8-6.2H24Z", EmblemInk.Main);
                break;

            case EmblemKind.Stay:
                using (k.Push(Affine.Group(16, 17.4, -5, 1.3))) Item(k, EmblemSubject.Image, back: false);
                using (k.Push(Affine.RotateAbout(-5, 16, 5.3))) k.Fill(EmblemPath.Rect(11.2, 2.6, 9.6, 5.4, .7), EmblemInk.Tape);
                break;

            case EmblemKind.After:
                k.Fill(EmblemPath.Polygon(StarPoints()), EmblemInk.Main);
                k.Fill(EmblemPath.Circle(16, 16, 3.6), EmblemInk.Cut);
                break;

            case EmblemKind.Splits:
                k.Stroke("M16 21v-4.5M16 16.5c0-3-7-1.5-7-5M16 16.5c0-3 7-1.5 7-5", EmblemInk.Soft, 2.2, true);
                using (k.Push(Affine.Group(16, 25.4, 0, .46))) Item(k, subject, back: true);
                using (k.Push(Affine.Group(8.6, 8.4, -8, .62))) Item(k, subject, back: false);
                using (k.Push(Affine.Group(23.4, 8.4, 8, .62))) Item(k, subject, back: false);
                break;

            case EmblemKind.Link:
                using (k.Push(Affine.RotateAbout(-38, 16, 16)))
                {
                    k.Stroke(EmblemPath.Rect(14.6, 11, 15, 10, 5), EmblemInk.Soft, 3.3, false);
                    k.Stroke(EmblemPath.Rect(2.4, 11, 15, 10, 5), EmblemInk.Main, 3.3, false);
                }
                break;

            case EmblemKind.Long:
                k.Fill("M9.5 6h13v3.4c0 3-4 4.6-4 6.6s4 3.6 4 6.6V26h-13v-3.4c0-3 4-4.6 4-6.6s-4-3.6-4-6.6z", EmblemInk.Soft);
                k.Fill("M11.8 25.2c0-2.4 4.2-3.6 4.2-5.6 0 2 4.2 3.2 4.2 5.6zM12.6 9.4h6.8c-.6 2.1-3.4 2.9-3.4 4.4 0-1.5-2.8-2.3-3.4-4.4z", EmblemInk.Main);
                k.Fill(EmblemPath.Rect(6.5, 2.8, 19, 3.6, 1.8), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(6.5, 25.6, 19, 3.6, 1.8), EmblemInk.Main);
                break;

            case EmblemKind.Big:
                k.Fill(EmblemPath.Rect(3.5, 3.5, 25, 25, 4.5), EmblemInk.Soft);
                k.Fill(EmblemPath.Rect(3.5, 15, 13.5, 13.5, 3.2), EmblemInk.Main);
                k.Stroke("M18.5 13.5l6.5-6.5M19.6 6.4h6v6", EmblemInk.Main, 2.7, true);
                break;

            case EmblemKind.Clear:
                k.Fill("M16 2.6c5.2 6.6 9.4 11 9.4 16a9.4 9.4 0 0 1-18.8 0c0-5 4.2-9.4 9.4-16z", EmblemInk.Soft);
                k.Fill("M16 2.6c5.2 6.6 9.4 11 9.4 16a9.4 9.4 0 0 1-9.4 9.4z", EmblemInk.Main);
                break;

            case EmblemKind.Loud:
                k.Fill("M3.6 12.4h5.2l7.4-6.2v19.6l-7.4-6.2H3.6z", EmblemInk.Main);
                k.Stroke("M20 11.8c1.7 1 2.6 2.5 2.6 4.2s-.9 3.2-2.6 4.2", EmblemInk.Main, 2.6, true);
                k.Stroke("M23.6 7.6c3.2 2.1 4.8 5 4.8 8.4s-1.6 6.3-4.8 8.4", EmblemInk.Soft, 2.6, true);
                break;

            case EmblemKind.Fast:
                k.Stroke("M4.6 23a11.4 11.4 0 0 1 22.8 0", EmblemInk.Soft, 3.6, true);
                k.Stroke("M16 23l6.4-9.6", EmblemInk.Main, 3.1, true);
                k.Fill(EmblemPath.Circle(16, 23, 2.8), EmblemInk.Main);
                break;

            case EmblemKind.Where:
                Monitor(k);
                k.Fill(EmblemPath.Rect(20.4, 8.2, 5, 5.6, 1), EmblemInk.Main);
                k.Fill(EmblemPath.Circle(8, 10.4, 1.1), EmblemInk.Soft);
                k.Fill(EmblemPath.Circle(8, 17.8, 1.1), EmblemInk.Soft);
                k.Fill(EmblemPath.Circle(24, 17.8, 1.1), EmblemInk.Soft);
                break;

            case EmblemKind.Screens:
                k.Fill(EmblemPath.Rect(11, 3, 19, 14, 2.8), EmblemInk.Soft);
                k.Fill(EmblemPath.Rect(9.5, 22.4, 5, 3.6), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(6, 25.4, 12, 2.4, 1.2), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(2, 9, 20, 14.4, 2.8), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(4.4, 11.4, 15.2, 9.6, 1.2), EmblemInk.Cut);
                break;

            case EmblemKind.Colour:
                // The mockup's three fixed dots; the back two at opacity .92 (alpha 0xEB).
                k.Fixed(EmblemPath.Circle(16, 10.4, 7.6), 0xFFFF69B4);
                k.Fixed(EmblemPath.Circle(10, 20.4, 7.6), 0xEB5CE0A5);
                k.Fixed(EmblemPath.Circle(22, 20.4, 7.6), 0xEB5EA8FF);
                break;

            case EmblemKind.When:
                k.Fill(EmblemPath.Rect(8.6, 2.4, 3.4, 6.4, 1.7), EmblemInk.Soft);
                k.Fill(EmblemPath.Rect(20, 2.4, 3.4, 6.4, 1.7), EmblemInk.Soft);
                k.Fill(EmblemPath.Rect(3.6, 5.6, 24.8, 23, 4.4), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(6.2, 12.2, 19.6, 13.8, 2), EmblemInk.Cut);
                foreach (var (x, y) in new[] { (11.0, 16.6), (16.0, 16.6), (21.0, 16.6), (11.0, 21.6) })
                    k.Fill(EmblemPath.Circle(x, y, 1.6), EmblemInk.Main);
                break;

            case EmblemKind.Safe:
                k.Fill("M16 2.6l11.4 4.2v8.4c0 7.2-4.8 12-11.4 14.4C9.4 27.2 4.6 22.4 4.6 15.2V6.8z", EmblemInk.Main);
                k.Stroke("M10.8 15.6l3.8 3.8 6.8-7.4", EmblemInk.Cut, 3.1, true);
                break;

            case EmblemKind.Motion:
                k.Stroke("M3.6 23c4-13 8 6 12-6s7 5 10.4-5.6", EmblemInk.Soft, 3.2, true);
                k.Fill(EmblemPath.Circle(26.6, 9.4, 3.8), EmblemInk.Main);
                break;

            case EmblemKind.Reacts:
                k.Stroke("M22.6 21.4l2.6 2.6M25.2 18.6h3.4M19.8 24.2v3.4", EmblemInk.Soft, 2.2, true);
                using (k.Push(Affine.Group(11.4, 17.6, -9, .98))) Item(k, EmblemSubject.Image, back: false);
                using (k.Push(Affine.Group(22.4, 10.2, 0, .86))) Item(k, EmblemSubject.Bubble, back: false);
                break;

            case EmblemKind.Eye:
                k.Fill("M2.4 16c3.7-6.2 8.2-9.3 13.6-9.3S25.9 9.8 29.6 16c-3.7 6.2-8.2 9.3-13.6 9.3S6.1 22.2 2.4 16z", EmblemInk.Main);
                k.Fill(EmblemPath.Circle(16, 16, 5.6), EmblemInk.Cut);
                k.Fill(EmblemPath.Circle(16, 16, 2.7), EmblemInk.Main);
                break;

            case EmblemKind.Glow:
                k.Stroke("M16 1.4v3.4M5.2 5.4l2.4 2.4M26.8 5.4l-2.4 2.4M1.6 16.6h3.4M27 16.6h3.4", EmblemInk.Soft, 2.4, true);
                using (k.Push(Affine.Group(16, 17.6, 0, 1.05))) Item(k, EmblemSubject.Image, back: false);
                break;

            case EmblemKind.Solid:
                k.Fill("M9 9h14a7 7 0 0 1 7 7v4.5a4.5 4.5 0 0 1-8.3 2.4L20.5 21h-9l-1.2 1.9A4.5 4.5 0 0 1 2 20.5V16a7 7 0 0 1 7-7z", EmblemInk.Main);
                k.Fill(EmblemPath.Rect(6.6, 14.3, 6.8, 2.4, .8), EmblemInk.Cut);
                k.Fill(EmblemPath.Rect(8.8, 12.1, 2.4, 6.8, .8), EmblemInk.Cut);
                k.Fill(EmblemPath.Circle(20.6, 13.8, 1.6), EmblemInk.Cut);
                k.Fill(EmblemPath.Circle(24.2, 17, 1.6), EmblemInk.Cut);
                break;

            case EmblemKind.Keepout:
                Monitor(k);
                k.Stroke(EmblemPath.Circle(16, 14, 4.4), EmblemInk.Main, 1.9, false);
                k.Stroke("M12.9 17.1l6.2-6.2", EmblemInk.Main, 1.9, true);
                k.Fill(EmblemPath.Rect(5.8, 7.8, 3.4, 4.2, .8), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(22.8, 16, 3.4, 4.2, .8), EmblemInk.Main);
                break;

            case EmblemKind.Corners:
                k.Fill(EmblemPath.Rect(9.5, 9.5, 19, 19, 6), EmblemInk.Soft);
                k.Stroke("M5 27V13a8 8 0 0 1 8-8h14", EmblemInk.Main, 3.6, true);
                break;

            case EmblemKind.Drag:
                k.Stroke("M13.6 4 16 1.6 18.4 4M13.6 28 16 30.4 18.4 28M4 13.6 1.6 16 4 18.4M28 13.6 30.4 16 28 18.4", EmblemInk.Main, 2.4, true);
                using (k.Push(Affine.Group(16, 16, 0, .8))) Item(k, EmblemSubject.Image, back: false);
                break;

            case EmblemKind.Shatter:
                k.Fill("M6 4h10.5l-3.4 9.6L6 15.6z", EmblemInk.Main);
                k.Fill("M19 4h7v13l-9.4-2.6z", EmblemInk.Soft);
                k.Fill("M6 18.6l7.6-2 3 11.4H6z", EmblemInk.Soft);
                k.Fill("M16.6 17.4L26 20v8h-6.6z", EmblemInk.Main);
                break;

            case EmblemKind.Jackpot:
                k.Fill(EmblemPath.Rect(2, 7.5, 28, 17, 4.4), EmblemInk.Main);
                foreach (double x in new[] { 4.6, 13, 21.4 })
                    k.Fill(EmblemPath.Rect(x, 10.2, 6, 11.6, 1.5), EmblemInk.Cut);
                foreach (double x in new[] { 7.6, 16, 24.4 })
                    k.Fill(EmblemPath.Circle(x, 16, 1.8), EmblemInk.Main);
                break;

            case EmblemKind.Flip:
                k.Stroke("M9 6.2c4.2-3.8 9.8-3.8 14 0M23.2 6.4V2.6M23.2 6.4h-3.8", EmblemInk.Soft, 2.3, true);
                using (k.Push(Affine.Group(11.4, 19, -13, 1))) k.Fill(EmblemPath.Rect(-5.6, -7.6, 11.2, 15.2, 2), EmblemInk.Soft);
                using (k.Push(Affine.Group(20.4, 19, 9, 1)))
                {
                    k.Fill(EmblemPath.Rect(-5.6, -7.6, 11.2, 15.2, 2), EmblemInk.Main);
                    k.Fill("M0-3.4 2.6 0 0 3.4-2.6 0z", EmblemInk.Cut);
                }
                break;

            case EmblemKind.Much:
                k.Fill(EmblemPath.Rect(3, 19, 5.6, 9, 1.6), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(10.4, 14, 5.6, 14, 1.6), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(17.8, 9, 5.6, 19, 1.6), EmblemInk.Soft);
                k.Fill(EmblemPath.Rect(25.2, 4, 4.2, 24, 1.6), EmblemInk.Soft);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
        return k.Layers;
    }

    /// <summary>
    /// The cache identity of a drawing: two calls with the same key draw the same layers. Subject
    /// only counts for Many and Splits; value is folded to what the art can show (Many and Most to
    /// their counts, Often to whole degrees of the hand), so a slider drag does not fill a cache.
    /// </summary>
    public static (EmblemKind Kind, EmblemSubject Subject, double Value) ShapeKey(EmblemKind kind, EmblemSubject subject, double value)
    {
        var s = kind is EmblemKind.Many or EmblemKind.Splits ? subject : EmblemSubject.Image;
        double v = kind switch
        {
            EmblemKind.Many => ManyCount(value),
            EmblemKind.Most => MostCount(value),
            EmblemKind.Often => Math.Round(OftenFraction(value) * 360) / 360,
            _ => 0,
        };
        return (kind, s, v);
    }

    /// <summary>Items drawn by Many: 1 -> 1, 2 -> 2, 3..5 -> 3, 6+ -> 4; below 1 (unset) the mockup's 3.</summary>
    public static int ManyCount(double value)
    {
        if (double.IsNaN(value) || value < 1) return 3;
        int n = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return n <= 2 ? Math.Max(n, 1) : n <= 5 ? 3 : 4;
    }

    /// <summary>Small rects drawn by Most: min(value, 8); below 1 (unset) the mockup's 8.</summary>
    public static int MostCount(double value)
    {
        if (double.IsNaN(value) || value < 1) return 8;
        return Math.Min((int)Math.Round(value, MidpointRounding.AwayFromZero), 8);
    }

    private static double OftenFraction(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    // ---------- shared pieces ----------

    private static void Item(Sketch k, EmblemSubject subject, bool back)
    {
        switch (subject)
        {
            case EmblemSubject.Video:
                if (back) { k.Fill(EmblemPath.Rect(-8.5, -6, 17, 12, 2.4), EmblemInk.Soft); break; }
                k.Fill(EmblemPath.Rect(-8.5, -6, 17, 12, 2.4), EmblemInk.Main);
                k.Fill("M-2.4-3.3 3.8 0-2.4 3.3Z", EmblemInk.Cut);
                break;
            case EmblemSubject.Bubble:
                if (back) { k.Fill(EmblemPath.Circle(0, 0, 7), EmblemInk.Soft); break; }
                k.Fill(EmblemPath.Circle(0, 0, 7), EmblemInk.Main);
                k.Fill(EmblemPath.Circle(-2.5, -2.6, 1.8), EmblemInk.Cut);
                break;
            case EmblemSubject.Word:
                if (back) { k.Fill(EmblemPath.Rect(-9.5, -5.5, 19, 11, 5.5), EmblemInk.Soft); break; }
                k.Fill(EmblemPath.Rect(-9.5, -5.5, 19, 11, 5.5), EmblemInk.Main);
                k.Fill(EmblemPath.Rect(-5.5, -2.4, 11, 1.9, .95), EmblemInk.Cut);
                k.Fill(EmblemPath.Rect(-5.5, .7, 7, 1.9, .95), EmblemInk.Cut);
                break;
            default:
                if (back) { k.Fill(EmblemPath.Rect(-6.5, -8, 13, 16, 1.8), EmblemInk.Soft); break; }
                k.Fill(EmblemPath.Rect(-6.5, -8, 13, 16, 1.8), EmblemInk.Paper);
                k.Fill(EmblemPath.Rect(-4.7, -6.2, 9.4, 9.4, .8), EmblemInk.Pic);
                break;
        }
    }

    private static void Monitor(Sketch k)
    {
        k.Fill(EmblemPath.Rect(12.5, 23, 7, 4), EmblemInk.Main);
        k.Fill(EmblemPath.Rect(8.5, 26.2, 15, 2.6, 1.3), EmblemInk.Main);
        k.Fill(EmblemPath.Rect(2, 4, 28, 20, 3.4), EmblemInk.Main);
        k.Fill(EmblemPath.Rect(4.6, 6.6, 22.8, 14.8, 1.6), EmblemInk.Cut);
    }

    /// <summary>The mockup's 16-point star, each coordinate rounded to one decimal as its toFixed(1).</summary>
    private static List<(double, double)> StarPoints()
    {
        var p = new List<(double, double)>(16);
        for (int i = 0; i < 16; i++)
        {
            double r = i % 2 == 1 ? 7.4 : 13.6, a = Math.PI * i / 8 - Math.PI / 2;
            p.Add((Math.Round(16 + r * Math.Cos(a), 1, MidpointRounding.AwayFromZero),
                   Math.Round(16 + r * Math.Sin(a), 1, MidpointRounding.AwayFromZero)));
        }
        return p;
    }

    /// <summary>Collects layers under a stack of group transforms.</summary>
    private sealed class Sketch
    {
        private readonly List<EmblemLayer> _layers = new();
        private Affine _m = Affine.Identity;
        public IReadOnlyList<EmblemLayer> Layers => _layers;

        public IDisposable Push(Affine local)
        {
            var saved = _m;
            _m = local.Then(_m);
            return new Pop(() => _m = saved);
        }

        public void Fill(string d, EmblemInk ink) =>
            _layers.Add(new EmblemLayer(EmblemPath.Transform(d, _m), ink, 0, false, null));

        public void Fixed(string d, uint argb) =>
            _layers.Add(new EmblemLayer(EmblemPath.Transform(d, _m), EmblemInk.Fixed, 0, false, argb));

        public void Stroke(string d, EmblemInk ink, double width, bool roundCaps) =>
            _layers.Add(new EmblemLayer(EmblemPath.Transform(d, _m), ink, width * _m.ScaleFactor, roundCaps, null));

        private sealed class Pop : IDisposable
        {
            private Action? _undo;
            public Pop(Action undo) => _undo = undo;
            public void Dispose() { _undo?.Invoke(); _undo = null; }
        }
    }
}
