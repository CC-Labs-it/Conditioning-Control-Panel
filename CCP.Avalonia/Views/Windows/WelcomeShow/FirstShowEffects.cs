using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowEffects.cs: the show's private, disposable
/// effect layer; never registers a session or writes a setting. Timeline, slots and gather spiral (Tick :106, Spawn
/// :167, Animate :211) are WPF's, timings from <see cref="S"/>. WPF stepped the production compositor layers, none of
/// which exist on this head, so <see cref="Render"/> draws with plain calls (no Effect: P82): pictures, bubbles,
/// pink tint, spiral, floating words, subliminal flash, the ChaosFxLayer sparks (burst, ripple, trail), gather, cues.
/// Brain drain: WPF blurred a live desktop capture (BrainDrainCapturePump); this head has no capture, so the
/// 11-15 s beat is a milky veil with the same .3 s edges, kept lighter than WPF's blur alpha (see DrainAlpha).</summary>
internal sealed class FirstShowEffects : Control, IDisposable
{
    private sealed record Media(List<Bitmap> Frames, double Delay);
    private sealed class Actor
    {
        public int Id, MediaIndex = -1, Slot = -1, Frame;
        public double Born, Life, X, Y, W, H, Angle, Alpha, Scale = 1, HomeX, HomeY, GatherX, GatherY, NextTrail;
        public bool Gathering, Gone, Bubble; public Media? Media; public string? Text;
    }
    /// <summary>One ChaosFxLayer particle: a spark flying out, or a ring (ripple) growing.</summary>
    private readonly record struct Spark(double X, double Y, double Vx, double Vy, double Born, double Life, double Size, Color Color, double Ring = 0);
    private static readonly double[] Xs = { .15, .82, .25, .74, .13, .86, .42, .60 };
    private static readonly double[] Ys = { .25, .66, .76, .23, .51, .42, .20, .79 };
    private readonly List<Media> _media = new();
    private readonly List<Actor> _actors = new();
    private readonly List<Spark> _sparks = new();
    private readonly Dictionary<(string, double), Geometry> _glyphs = new();
    private readonly Dictionary<(Color, int), ImmutableSolidColorBrush> _brushes = new();
    private readonly Dictionary<(Color, int), ImmutablePen> _pens = new();
    private readonly Action<string> _cue;
    private FirstShowDeck? _deck;
    private Media? _spiral;
    private double _time, _nextFlash = S.FlashAt, _nextBubble = S.BubblesAt, _nextSub = S.WordsAt, _subAt = -10;
    private int _serial;
    private bool _disposed, _revealed, _wordsSpawned;
    private const int MaxSparks = 900;
    /// <summary>Test seam: MotionFx.AllowParticles.</summary>
    internal Func<bool> Particles = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles;
    internal int SparkCount => _sparks.Count;
    internal int WordCount => _actors.Count(a => a.Text != null);
    internal bool HasSpiral => _spiral != null;
    /// <summary>The subliminal word's opacity now (WPF: in 100 ms, hold 70, out 180, at 70%).</summary>
    internal double SubliminalAlpha
    {
        get { double t = _time - _subAt; return t < 0 || t >= .35 ? 0 : .7 * Math.Clamp(t < .1 ? t / .1 : t < .17 ? 1 : 1 - (t - .17) / .18, 0, 1); }
    }
    /// <summary>The brain-drain veil's opacity now, .3 s edges as WPF. Deliberately 70/255 (~.27), not WPF
    /// BrainDrainLayer.AlphaFor(70) (~.69): that alpha laid a blurred copy of the desktop over itself, while here
    /// the veil is a flat milky colour, which at .69 would white the screen out.</summary>
    internal double DrainAlpha
    {
        get
        {
            if (_time < S.DrainAt || _time >= S.WordsAt) return 0;
            bool first = _time < S.DrainDeepAt;
            return 70 / 255.0 * Math.Clamp(Math.Min(_time - (first ? S.DrainAt : S.DrainDeepAt), (first ? S.DrainDeepAt : S.WordsAt) - _time) / .3, 0, 1);
        }
    }
    public FirstShowEffects(Action<string> cue) { _cue = cue; IsHitTestVisible = false; }
    internal FirstShowEffects() : this(_ => { }) { }   // --render-all: an empty layer (no deck loaded)
    private double W => Math.Max(1, double.IsNaN(Width) ? Bounds.Width : Width);
    private double H => Math.Max(1, double.IsNaN(Height) ? Bounds.Height : Height);
    private static MotionLevel Motion => CoreSettings.Current?.MotionLevel ?? MotionLevel.Full;
    public async Task LoadAsync(IReadOnlyList<string> paths)
    {
        var loaded = await Task.Run(() => paths.Take(FirstShowMediaLoader.TargetPictures).Select(p => Decode(p)).OfType<Media>().ToList());
        if (_disposed) { Free(loaded); return; }
        if (loaded.Count == 0) throw new InvalidOperationException("No preview images could be decoded.");
        _media.AddRange(loaded); _deck = new FirstShowDeck(_media.Count);
        // WPF LoadAsync: the shipped spiral, decoded small (512 px) for the pink beat.
        var spiral = await Task.Run(() => Decode(Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif"), 512));
        if (_disposed) { if (spiral != null) Free(new[] { spiral }); } else _spiral = spiral;
    }
    private static Media? Decode(string path) => Decode(path, 600);
    private static Media? Decode(string path, int edge) =>
        SpiralOverlay.Decode(path, edge) is var (frames, delay) && frames.Count > 0 ? new Media(frames, Math.Max(.025, delay.TotalSeconds)) : null;
    public void Tick(double seconds, double delta)
    {
        if (_disposed) return;
        _time = seconds;
        if (seconds >= _nextBubble && seconds < S.GatherAt && _media.Count > 0) { Spawn(true, seconds); _nextBubble = seconds + S.BubbleEvery; }
        if (seconds >= _nextFlash && seconds < S.GatherAt && _media.Count > 0)
        {
            Spawn(false, seconds); _nextFlash = seconds + (seconds < S.FastFlashUntil ? S.FastFlashEvery : S.FlashEvery);
        }
        if (seconds >= S.WordsAt && !_wordsSpawned)
        {
            _wordsSpawned = true;
            SpawnWord(Loc.Get("first_show_word1"), 0); SpawnWord(Loc.Get("first_show_word2"), 1);
        }
        if (seconds >= _nextSub && seconds < S.SubliminalUntil) { _subAt = seconds; _nextSub = seconds + S.SubliminalEvery; }
        bool moving = Motion != MotionLevel.Off;
        foreach (var a in _actors) if (!a.Gone) Animate(a, seconds, moving);
        _actors.RemoveAll(a => a.Gone);
        _sparks.RemoveAll(p => seconds - p.Born >= p.Life);
        if (seconds >= S.RevealAt && !_revealed)
        {
            _revealed = true; _subAt = -10;
            if (Particles())
            {
                for (int i = 0; i < 6; i++) Burst(W * .5, H * .56, i % 2 == 0 ? Colors.HotPink : Colors.Gold, 1.1 + i * .18);
                Ripple(W * .5, H * .56, W * .25, .9);
            }
            _cue("reveal");
        }
        InvalidateVisual();
    }
    private void Spawn(bool bubble, double born)
    {
        var visible = _actors.Where(a => a.MediaIndex >= 0 && !a.Gone && born - a.Born < a.Life).Select(a => (a.MediaIndex, a.Slot)).ToArray();
        if (bubble && _actors.Count(a => a.Bubble && !a.Gone && born - a.Born < a.Life) >= 3) return;
        if (_deck?.Next(visible) is not { } pick) return;
        int id = _serial++;
        var media = _media[pick.Media];
        var size = media.Frames[0].Size;
        double w = Math.Min(W * .19, 300), h = w * size.Height / Math.Max(1, size.Width);
        if (h > H * .30) { w *= H * .30 / h; h = H * .30; }
        var a = new Actor
        {
            Id = id, MediaIndex = pick.Media, Slot = pick.Slot, Born = born, Life = bubble ? 9 : 4.1 + id % 3 * .27, Media = media, Bubble = bubble,
            HomeX = W * (Xs[pick.Slot] + (Random.Shared.NextDouble() - .5) * .05), HomeY = H * (Ys[pick.Slot] + (Random.Shared.NextDouble() - .5) * .04), W = w, H = h
        };
        if (bubble) a.W = a.H = Math.Min(W * .095, 155);
        _actors.Add(a);
        if (Particles()) Burst(a.HomeX, a.HomeY, bubble ? Colors.Cyan : Colors.HotPink, .7);
        _cue(bubble ? "bubbles" : "flash");
    }
    // WPF SpawnWord (:203): the two words drift for the rest of the show.
    private void SpawnWord(string text, int slot) => _actors.Add(new Actor
    {
        Id = _serial++, Born = S.WordsAt + slot * .6, Life = 30, Text = text, W = W * .24, H = 70, HomeX = W * (.25 + slot * .5), HomeY = H * (.23 + slot * .5)
    });
    private void Animate(Actor a, double time, bool moving)
    {
        double age = time - a.Born;
        if (age < 0) { a.Alpha = 0; return; }
        double motion = Motion == MotionLevel.Full ? 1 : moving ? .4 : 0;
        a.X = a.HomeX + Math.Sin(age * (.9 + a.Id % 4 * .19) + a.Id) * W * .023 * motion;
        a.Y = a.HomeY + Math.Cos(age * (1.1 + a.Id % 3 * .17) + a.Id * .7) * H * .025 * motion;
        if (a.Text != null && moving)
        {
            a.X = W * (.15 + Triangle(age * (.085 + a.Id % 3 * .018) + a.Id * .37) * .70);
            a.Y = H * (.12 + Triangle(age * (.11 + a.Id % 2 * .03) + a.Id * .29) * .66);
        }
        a.Angle = Math.Sin(age * (2.1 + a.Id % 4 * .27) + a.Id) * 5 * motion;
        a.Alpha = Math.Clamp(age / .3, 0, 1);
        a.Scale = moving ? 1 + Math.Sin(Math.Min(1, age / .45) * Math.PI) * .12 : 1;
        if (time < S.GatherAt)
        {
            a.Alpha *= Math.Clamp((a.Life - age) / .7, 0, 1);
            if (age >= a.Life) a.Gone = true;
        }
        else
        {
            if (!a.Gathering) { a.Gathering = true; a.GatherX = a.X; a.GatherY = a.Y; }
            double p = Math.Clamp((time - S.GatherAt - Fraction(a.Id * .618034) * 1.05) / (1.7 + Fraction(a.Id * .414214) * .9), 0, 1);
            double remain = Math.Pow(1 - p, 1.22), dx = a.GatherX - W * .5, dy = a.GatherY - H * .56;
            double turn = p * (4.8 + Fraction(a.Id * .732051) * 5.2) * motion;
            a.X = W * .5 + (dx * Math.Cos(turn) - dy * Math.Sin(turn)) * remain + Math.Sin(time * (13 + a.Id % 5) + a.Id) * 10 * remain * motion;
            a.Y = H * .56 + (dx * Math.Sin(turn) + dy * Math.Cos(turn)) * remain + Math.Cos(time * (11 + a.Id % 7) + a.Id) * 8 * remain * motion;
            a.Angle += p * (a.Id % 2 == 0 ? 140 : -190) * motion;
            a.Scale = moving ? Math.Max(.01, 1 - Math.Pow(p, 1.5)) : 1; a.Alpha *= 1 - Math.Pow(p, 5);
            if (!moving) { a.X = a.GatherX; a.Y = a.GatherY; }
            if (p >= 1) { a.Gone = true; if (Particles()) Burst(a.X, a.Y, Colors.Gold, .45); }
        }
        if (Particles() && time >= a.NextTrail && !a.Gone && (time >= S.GatherAt || a.Bubble))
        {
            Trail(a.X, a.Y, time >= S.GatherAt ? .52 : .32, a.Id % 3 == 0, W * .5 - a.X, H * .56 - a.Y);
            a.NextTrail = time + (time >= S.GatherAt ? .045 : .16);
        }
        a.Frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
    }
    // Match Pop's circular hit predicate in local physical pixels, like BubbleOverlay's native regions.
    internal PixelRect[] BubbleInputRects(double scale)
    {
        var rects = new List<PixelRect>();
        if (_disposed || _time >= S.GatherAt) return rects.ToArray();
        foreach (var a in _actors.Where(a => a.Bubble && !a.Gone))
        {
            double r = a.W * .52;
            for (int y = Math.Max(0, (int)Math.Ceiling((a.Y - r) * scale)); y < Math.Min(H * scale, (a.Y + r) * scale); y++)
            {
                double dy = y / scale - a.Y, half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                int left = Math.Max(0, (int)Math.Floor((a.X - half) * scale) + 1);
                int right = Math.Min((int)(W * scale) - 1, (int)Math.Ceiling((a.X + half) * scale) - 1);
                if (right >= left) rects.Add(new PixelRect(left, y, right - left + 1, 1));
            }
        }
        return rects.ToArray();
    }
    /// <summary>WPF Pop (:310): a click on a live bubble pops it.</summary>
    public void Pop(double x, double y)
    {
        if (_disposed || _time >= S.GatherAt) return;
        var a = _actors.LastOrDefault(a => a.Bubble && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.W * .52, 2));
        if (a == null) return;
        a.Gone = true;
        if (Particles()) { Burst(a.X, a.Y, Colors.HotPink, 1.3); Ripple(a.X, a.Y, a.W, .5); }
        _cue("pop");
    }
    // The three ChaosFxLayer emitters the show used (EmitBurst, EmitRipple, EmitTrail), drawn plain.
    private void Burst(double x, double y, Color color, double power)
    {
        for (int i = 0, n = (int)(14 * power) + 6; i < n && _sparks.Count < MaxSparks; i++)
        {
            double angle = Random.Shared.NextDouble() * Math.PI * 2, v = (60 + Random.Shared.NextDouble() * 170) * power;
            _sparks.Add(new Spark(x, y, Math.Cos(angle) * v, Math.Sin(angle) * v, _time, .45 + Random.Shared.NextDouble() * .5, 2 + Random.Shared.NextDouble() * 3 * power, color));
        }
    }
    private void Ripple(double x, double y, double radius, double life) { if (_sparks.Count < MaxSparks) _sparks.Add(new Spark(x, y, 0, 0, _time, life, 0, Colors.HotPink, radius)); }
    private void Trail(double x, double y, double life, bool gold, double towardX, double towardY)
    {
        if (_sparks.Count >= MaxSparks) return;
        double len = Math.Max(1, Math.Sqrt(towardX * towardX + towardY * towardY));
        _sparks.Add(new Spark(x + (Random.Shared.NextDouble() - .5) * 14, y + (Random.Shared.NextDouble() - .5) * 14, towardX / len * 26, towardY / len * 26,
            _time, life, 2.2 + Random.Shared.NextDouble() * 2, gold ? Colors.Gold : Colors.HotPink));
    }
    public override void Render(DrawingContext dc)
    {
        if (_disposed) return;
        var all = new Rect(0, 0, W, H);
        if (DrainAlpha > 0) dc.FillRectangle(new ImmutableSolidColorBrush(Color.FromRgb(236, 228, 244), DrainAlpha), all);
        if (_time >= S.PinkAt && _time < S.RevealAt)
        {
            double fade = Math.Clamp((_time - S.PinkAt) / .8, 0, 1) * Math.Clamp((S.RevealAt - _time) / 3, 0, 1);
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(255, 65, 165), .13 * fade), all);
            if (_spiral != null && fade > 0)
            {
                // WPF SpiralLayer at 16%, UniformToFill over the stage; frame 0 when motion is off.
                var frame = _spiral.Frames[Motion != MotionLevel.Off ? (int)(_time / _spiral.Delay) % _spiral.Frames.Count : 0];
                double k = Math.Max(W / Math.Max(1, frame.Size.Width), H / Math.Max(1, frame.Size.Height));
                double sw = frame.Size.Width * k, sh = frame.Size.Height * k;
                using (dc.PushOpacity(.16 * fade)) dc.DrawImage(frame, new Rect((W - sw) / 2, (H - sh) / 2, sw, sh));
            }
        }
        foreach (var a in _actors)
        {
            if (a.Gone || a.Alpha < .002) continue;
            var m = Matrix.CreateTranslation(-a.X, -a.Y) * Matrix.CreateScale(a.Scale, a.Scale)
                  * Matrix.CreateRotation(a.Angle * Math.PI / 180) * Matrix.CreateTranslation(a.X, a.Y);
            using (dc.PushTransform(m))
            using (dc.PushOpacity(a.Alpha))
            {
                var box = new Rect(a.X - a.W / 2, a.Y - a.H / 2, a.W, a.H);
                if (a.Text != null) DrawOutlined(dc, a.Text, Math.Min(56, W * .035), a.X, a.Y, Colors.HotPink, 6);
                else if (a.Bubble)
                {
                    var inner = box.Deflate(a.W * .135);
                    using (dc.PushGeometryClip(new EllipseGeometry(inner))) dc.DrawImage(a.Media!.Frames[a.Frame], inner);
                    dc.DrawEllipse(new SolidColorBrush(Colors.White, .12), new Pen(new SolidColorBrush(Colors.HotPink, .45), 6), box.Deflate(3));
                    dc.DrawEllipse(new SolidColorBrush(Colors.White, .28), null, new Rect(box.X + a.W * .22, box.Y + a.H * .16, a.W * .2, a.H * .12));
                }
                else
                {
                    using (dc.PushClip(new RoundedRect(box, 14))) dc.DrawImage(a.Media!.Frames[a.Frame], box);
                    dc.DrawRectangle(null, new Pen(Brushes.HotPink, 4), new RoundedRect(box, 14));
                }
            }
        }
        if (SubliminalAlpha > 0)
            using (dc.PushOpacity(SubliminalAlpha)) DrawOutlined(dc, Loc.Get("first_show_word1"), Math.Min(180, W * .11), W / 2, H / 2, Color.FromRgb(245, 50, 175), 8);
        foreach (var p in _sparks)
        {
            double t = (_time - p.Born) / p.Life, age = _time - p.Born;
            if (t < 0 || t >= 1) continue;
            if (p.Ring > 0)
            {
                double r = p.Ring * (1 - Math.Pow(1 - t, 3));
                int fade = (int)(32 * (1 - t));
                if (!_pens.TryGetValue((p.Color, fade), out var pen)) _pens[(p.Color, fade)] = pen = new ImmutablePen(new ImmutableSolidColorBrush(p.Color, .67 * fade / 32), 3);
                dc.DrawEllipse(null, pen, new Point(p.X, p.Y), r, r);
            }
            else
            {
                double r = p.Size * (1 - t * .6);
                int fade = (int)(32 * (1 - t));
                if (!_brushes.TryGetValue((p.Color, fade), out var fill)) _brushes[(p.Color, fade)] = fill = new ImmutableSolidColorBrush(p.Color, .9 * fade / 32);
                dc.DrawEllipse(fill, null, new Point(p.X + p.Vx * age * (1 - t * .4), p.Y + p.Vy * age * (1 - t * .4)), r, r);
            }
        }
    }
    /// <summary>WPF DrawText twice: a pink stroke under a white fill, centred on (x, y).</summary>
    private void DrawOutlined(DrawingContext dc, string text, double size, double x, double y, Color outline, double stroke)
    {
        if (!_glyphs.TryGetValue((text, size), out var glyphs))
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Arial", FontStyle.Normal, FontWeight.Bold), size, Brushes.White);
            glyphs = _glyphs[(text, size)] = ft.BuildGeometry(new Point(-ft.Width / 2, -ft.Baseline)) ?? new RectangleGeometry();
        }
        using (dc.PushTransform(Matrix.CreateTranslation(x, y)))
        {
            dc.DrawGeometry(null, new Pen(new ImmutableSolidColorBrush(outline, 220 / 255.0), stroke), glyphs);
            dc.DrawGeometry(Brushes.White, null, glyphs);
        }
    }
    private static double Fraction(double v) => v - Math.Floor(v);
    private static double Triangle(double v) => 1 - Math.Abs(Fraction(v) * 2 - 1);
    private static void Free(IEnumerable<Media> media) { foreach (var m in media) foreach (var f in m.Frames) f.Dispose(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _actors.Clear(); _sparks.Clear(); _glyphs.Clear(); Free(_media); _media.Clear();
        if (_spiral != null) Free(new[] { _spiral }); _spiral = null;
        InvalidateVisual();
    }
}
