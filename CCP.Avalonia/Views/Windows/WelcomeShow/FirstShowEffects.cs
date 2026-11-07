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
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.FirstShow;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowEffects.cs: the show's private, disposable
/// effect layer; never registers a session or writes a setting. Timeline, slots and gather spiral (Tick :106, Spawn
/// :167, Animate :211) are WPF's, timings from <see cref="S"/>. WPF stepped the production compositor layers, none of
/// which exist on this head, so <see cref="Render"/> draws with plain calls (no Effect: P82): pictures, bubbles,
/// pink tint, spiral, floating words, subliminal flash, gather, cues. The sparks (burst, ripple, trail) are WPF's own
/// Core <see cref="ChaosFxLayer"/> (WPF ChaosFxLayerWpf.Create :55), so counts, colours, additive bloom, the electric
/// ripple and timing are WPF's by construction; it is recorded into an SKPicture the render thread replays, as
/// ChaosSkiaFxOverlay does. The stage is in DIPs, so the layer's stage scale is 1 (WPF: physical px x DPI scale).
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
    private static readonly double[] Xs = { .15, .82, .25, .74, .13, .86, .42, .60 };
    private static readonly double[] Ys = { .25, .66, .76, .23, .51, .42, .20, .79 };
    private static readonly SkiaSharp.SKColor HotPink = new(0xFF, 0x69, 0xB4), Gold = new(0xFF, 0xD7, 0x00), Cyan = new(0x00, 0xFF, 0xFF);
    private readonly List<Media> _media = new();
    private readonly List<Actor> _actors = new();
    private readonly Dictionary<(string, double), Geometry> _glyphs = new();
    private readonly Action<string> _cue;
    private FirstShowDeck? _deck;
    private Media? _spiral;
    private double _time, _nextFlash = S.FlashAt, _nextBubble = S.BubblesAt, _nextSub = S.WordsAt, _subAt = -10;
    private int _serial;
    private bool _disposed, _revealed, _wordsSpawned;
    /// <summary>WPF's ChaosFxLayer: the show's spark field (tests read its counts and seed it).</summary>
    internal readonly ChaosFxLayer Fx;
    /// <summary>Test seam: MotionFx.AllowParticles (spawn sparks, trails, gather and reveal bursts).</summary>
    internal Func<bool> Particles = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles;
    /// <summary>Test seam: the gate of the pop burst, a user's own click (WPF Pop :313 asks MotionFx.AllowParticles).
    /// <see cref="global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowFeedbackParticles"/>:
    /// WPF's gate at the user's own settings; the CPU path's lighter tier does not remove it.</summary>
    internal Func<bool> PopParticles = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowFeedbackParticles;
    internal int SparkCount => Fx.ParticleCount + Fx.RippleCount;
    /// <summary>Tests: render the frame without the spark field, to diff the burst out of an otherwise identical frame.</summary>
    internal bool DrawSparks = true;
    /// <summary>Tests: the live bubbles' centres and widths, in stage DIPs.</summary>
    internal (double X, double Y, double W)[] LiveBubbles => _actors.Where(a => a.Bubble && !a.Gone).Select(a => (a.X, a.Y, a.W)).ToArray();
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
    public FirstShowEffects(Action<string> cue, Random? rng = null)
    {
        _cue = cue; IsHitTestVisible = false;
        Fx = new ChaosFxLayer(() => { }, rng: rng);   // Tick repaints every frame; nothing to wake
    }
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
        Fx.Update(TimeSpan.FromSeconds(Math.Clamp(delta, 0, .1)));   // WPF Tick :112, :117: before this frame's spawns
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
        if (seconds >= S.RevealAt && !_revealed)
        {
            _revealed = true; _subAt = -10;
            if (Particles())
            {
                for (int i = 0; i < 6; i++) Fx.EmitBurst(Pt(W * .5, H * .56), i % 2 == 0 ? HotPink : Gold, 1.1f + i * .18f);
                Fx.EmitRipple(Pt(W * .5, H * .56), W * .25, 900, true);
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
        if (Particles()) Fx.EmitBurst(Pt(a.HomeX, a.HomeY), bubble ? Cyan : HotPink, .7f);
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
            if (p >= 1) { a.Gone = true; if (Particles()) Fx.EmitBurst(Pt(a.X, a.Y), Gold, .45f); }
        }
        if (Particles() && time >= a.NextTrail && !a.Gone && (time >= S.GatherAt || a.Bubble))
        {
            Fx.EmitTrail(Pt(a.X, a.Y), time >= S.GatherAt ? .52 : .32, a.Id % 3 == 0, W * .5 - a.X, H * .56 - a.Y);
            a.NextTrail = time + (time >= S.GatherAt ? .045 : .16);
        }
        a.Frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
    }
    // Match Pop's circular hit predicate in local physical pixels, like BubbleOverlay's native regions.
    internal PixelRect[] BubbleInputRects(double scale)
    {
        var buf = Array.Empty<PixelRect>();
        int n = BubbleInputRects(scale, ref buf);   // separate statement: buf[..Call(ref buf)] reads the old array
        return buf[..n];
    }
    /// <summary>Fills <paramref name="rects"/> (grown, never shrunk) and returns the count; identical
    /// adjacent rows merge into one rect, as BubbleOverlay.cs does.</summary>
    internal int BubbleInputRects(double scale, ref PixelRect[] rects)
    {
        int n = 0;
        if (_disposed || _time >= S.GatherAt) return 0;
        foreach (var a in _actors)
        {
            if (!a.Bubble || a.Gone) continue;
            double r = a.W * .52;
            for (int y = Math.Max(0, (int)Math.Ceiling((a.Y - r) * scale)); y < Math.Min(H * scale, (a.Y + r) * scale); y++)
            {
                double dy = y / scale - a.Y, half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                int left = Math.Max(0, (int)Math.Floor((a.X - half) * scale) + 1);
                int right = Math.Min((int)(W * scale) - 1, (int)Math.Ceiling((a.X + half) * scale) - 1);
                if (right < left) continue;
                if (n > 0 && rects[n - 1] is var p && p.X == left && p.Width == right - left + 1 && p.Bottom == y)
                    rects[n - 1] = new PixelRect(p.X, p.Y, p.Width, p.Height + 1);
                else
                {
                    if (n == rects.Length) Array.Resize(ref rects, Math.Max(64, rects.Length * 2));
                    rects[n++] = new PixelRect(left, y, right - left + 1, 1);
                }
            }
        }
        return n;
    }
    /// <summary>WPF Pop (:307-319): a click on a live bubble pops it. The bubble goes at once and WPF's pop plays in
    /// its place: ChaosFxLayer.EmitBurst (white-cored flash, a ring of 12 sparks, 7-11 shards, hot pink, x1.3) and a
    /// strong EmitRipple (electric shock ring out to the bubble's width in 500 ms, 14 motes), then the "pop" cue.
    /// Direct feedback, so it asks <see cref="PopParticles"/>: the CPU path's lighter tier keeps it (owner, 11-06:
    /// "the bubble just disappeared when popped"); Reduced/Off motion and the user's own Performance mode drop it, as WPF.</summary>
    public void Pop(double x, double y)
    {
        if (_disposed || _time >= S.GatherAt) return;
        var a = _actors.LastOrDefault(a => a.Bubble && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.W * .52, 2));
        if (a == null) return;
        a.Gone = true;
        if (PopParticles()) { Fx.EmitBurst(Pt(a.X, a.Y), HotPink, 1.3f); Fx.EmitRipple(Pt(a.X, a.Y), a.W, 500, true); }
        _cue("pop");
        InvalidateVisual();
    }
    private static SkiaSharp.SKPoint Pt(double x, double y) => new((float)x, (float)y);
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
        // WPF Render :303: the spark field draws last, over the actors and the subliminal word.
        if (DrawSparks && Fx.IsActive)
        {
            using var rec = new SkiaSharp.SKPictureRecorder();
            var canvas = rec.BeginRecording(new SkiaSharp.SKRect(0, 0, (float)W, (float)H));
            Fx.Render(canvas, new SkiaSharp.SKRectI(0, 0, (int)W, (int)H), 1, TimeSpan.Zero);
            dc.Custom(new PictureOp(rec.EndRecording(), all));
        }
    }
    /// <summary>The recorded spark field, replayed on the render thread's Skia canvas (ChaosSkiaFxOverlay.PictureOp).</summary>
    private sealed class PictureOp : global::Avalonia.Rendering.SceneGraph.ICustomDrawOperation
    {
        private readonly SkiaSharp.SKPicture _picture;
        public PictureOp(SkiaSharp.SKPicture picture, Rect bounds) { _picture = picture; Bounds = bounds; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(global::Avalonia.Rendering.SceneGraph.ICustomDrawOperation? other) => false;
        public void Dispose() => _picture.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<global::Avalonia.Skia.ISkiaSharpApiLeaseFeature>() is not { } lease) return;
            using var leased = lease.Lease();
            leased.SkCanvas.DrawPicture(_picture);
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
        _actors.Clear(); Fx.Clear(); _glyphs.Clear(); Free(_media); _media.Clear();
        if (_spiral != null) Free(new[] { _spiral }); _spiral = null;
        InvalidateVisual();
    }
}
