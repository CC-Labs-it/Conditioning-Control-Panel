using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowEffects.cs: the show's private, disposable
/// effect layer; never registers a session or writes a setting. Timeline, slots and gather spiral (Tick :106, Spawn
/// :167, Animate :211) are WPF's, timings from <see cref="S"/>. WPF stepped the production compositor layers, none of
/// which exist on this head, so <see cref="Render"/> draws each with plain calls (no Effect: P82). ponytail: WPF's
/// brain drain blurred a live desktop capture (BrainDrainCapturePump); here 11-15 s is a milky veil with its edges.</summary>
internal sealed class FirstShowEffects : Control, IDisposable
{
    private sealed record Media(List<Bitmap> Frames, double Delay);
    private sealed class Actor
    {
        public int Id, MediaIndex = -1, Slot = -1, Frame;
        public double Born, Life, X, Y, W, H, Angle, Alpha, Scale = 1, HomeX, HomeY, GatherX, GatherY, NextTrail;
        public bool Gathering, Gone, Bubble; public Media? Media; public string? Text;
    }
    private sealed class Spark { public double X, Y, Vx, Vy, Born, Life, Size, Radius; public Color Color; public bool Ring; }
    private static readonly double[] Xs = { .15, .82, .25, .74, .13, .86, .42, .60 };
    private static readonly double[] Ys = { .25, .66, .76, .23, .51, .42, .20, .79 };
    private readonly List<Media> _media = new();
    private readonly List<Actor> _actors = new();
    private readonly List<Spark> _sparks = new();
    private readonly Action<string> _cue;
    private FirstShowDeck? _deck;
    private Media? _spiral;
    private double _time, _nextFlash = S.FlashAt, _nextBubble = S.BubblesAt, _nextSub = S.WordsAt, _subAt = -10;
    private int _serial;
    private bool _disposed, _wordsSpawned, _revealed;
    public FirstShowEffects(Action<string> cue) { _cue = cue; IsHitTestVisible = false; }
    internal FirstShowEffects() : this(_ => { }) { }   // --render-all: an empty layer (no deck loaded)
    private double W => Math.Max(1, double.IsNaN(Width) ? Bounds.Width : Width);
    private double H => Math.Max(1, double.IsNaN(Height) ? Bounds.Height : Height);
    private static MotionLevel Motion => CoreSettings.Current?.MotionLevel ?? MotionLevel.Full;
    private static bool Particles => AmbientFxCanvas.Env.AllowParticles;
    public async Task LoadAsync(IReadOnlyList<string> paths)
    {
        var loaded = await Task.Run(() => paths.Take(FirstShowMediaLoader.TargetPictures).Select(Decode).OfType<Media>().ToList());
        if (_disposed) { Free(loaded); return; }
        if (loaded.Count == 0) throw new InvalidOperationException("No preview images could be decoded.");
        _media.AddRange(loaded); _deck = new FirstShowDeck(_media.Count);
        var spiral = Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif");
        if (File.Exists(spiral)) _spiral = await Task.Run(() => Decode(spiral));
        if (_disposed && _spiral != null) { Free(new[] { _spiral }); _spiral = null; }
    }
    private static Media? Decode(string path) =>
        SpiralOverlay.Decode(path, 600) is var (frames, delay) && frames.Count > 0 ? new Media(frames, Math.Max(.025, delay.TotalSeconds)) : null;
    public void Tick(double seconds, double delta)
    {
        if (_disposed) return;
        _time = seconds;
        if (seconds >= _nextBubble && seconds < S.GatherAt && _media.Count > 0) { Spawn(true, seconds); _nextBubble = seconds + S.BubbleEvery; }
        if (seconds >= _nextFlash && seconds < S.GatherAt && _media.Count > 0)
        {
            Spawn(false, seconds); _nextFlash = seconds + (seconds < S.FastFlashUntil ? S.FastFlashEvery : S.FlashEvery);
        }
        if (seconds >= S.WordsAt && !_wordsSpawned) { _wordsSpawned = true; SpawnWord(Loc.Get("first_show_word1"), 0); SpawnWord(Loc.Get("first_show_word2"), 1); }
        if (seconds >= _nextSub && seconds < S.SubliminalUntil) { _subAt = seconds; _nextSub = seconds + S.SubliminalEvery; }
        bool moving = Motion != MotionLevel.Off;
        foreach (var a in _actors) if (!a.Gone) Animate(a, seconds, moving);
        _actors.RemoveAll(a => a.Gone);
        _sparks.RemoveAll(s => seconds - s.Born > s.Life);
        foreach (var s in _sparks) { s.X += s.Vx * delta; s.Y += s.Vy * delta; }
        if (seconds >= S.RevealAt && !_revealed)
        {
            _revealed = true; _subAt = -10;
            if (Particles)
                for (int i = 0; i < 6; i++) Burst(W * .5, H * .56, i % 2 == 0 ? Colors.HotPink : Colors.Gold, 1.1 + i * .18, true);
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
        if (Particles) Burst(a.HomeX, a.HomeY, bubble ? Colors.Cyan : Colors.HotPink, .7, false);
        _cue(bubble ? "bubbles" : "flash");
    }
    private void SpawnWord(string text, int slot) => _actors.Add(new Actor
    {
        Id = _serial++, Born = S.WordsAt + slot * .6, Life = 30, Text = text, W = W * .24, H = 70,
        HomeX = W * (.25 + slot * .5), HomeY = H * (.23 + slot * .5)
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
            if (p >= 1) { a.Gone = true; if (Particles) Burst(a.X, a.Y, Colors.Gold, .45, false); }
        }
        if (Particles && time >= a.NextTrail && !a.Gone && (time >= S.GatherAt || a.Bubble))
        {
            _sparks.Add(new Spark { X = a.X, Y = a.Y, Vx = (W * .5 - a.X) * .3, Vy = (H * .56 - a.Y) * .3, Born = time, Life = time >= S.GatherAt ? .52 : .32, Size = 3, Color = a.Id % 3 == 0 ? Colors.Gold : Colors.HotPink });
            a.NextTrail = time + (time >= S.GatherAt ? .045 : .16);
        }
        a.Frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
    }
    private void Burst(double x, double y, Color color, double power, bool ring)
    {
        for (int i = 0; i < 10; i++)
        {
            double angle = i * Math.PI / 5 + Random.Shared.NextDouble() * .4, speed = 120 * power * (.6 + Random.Shared.NextDouble() * .6);
            _sparks.Add(new Spark { X = x, Y = y, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed, Born = _time, Life = .6, Size = 3 * power, Color = color });
        }
        if (ring) _sparks.Add(new Spark { X = x, Y = y, Born = _time, Life = .9, Ring = true, Radius = W * .25, Color = color });
    }
    /// <summary>WPF Pop (:310): a click on a live bubble pops it.</summary>
    public void Pop(double x, double y)
    {
        if (_disposed || _time >= S.GatherAt) return;
        var a = _actors.LastOrDefault(a => a.Bubble && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.W * .52, 2));
        if (a == null) return;
        a.Gone = true;
        if (Particles) Burst(a.X, a.Y, Colors.HotPink, 1.3, true);
        _cue("pop");
    }
    public override void Render(DrawingContext dc)
    {
        if (_disposed) return;
        var all = new Rect(0, 0, W, H);
        if (_time >= S.DrainAt && _time < S.WordsAt)
        {
            double edge = Math.Min(_time - (_time < S.DrainDeepAt ? S.DrainAt : S.DrainDeepAt), (_time < S.DrainDeepAt ? S.DrainDeepAt : S.WordsAt) - _time);
            dc.FillRectangle(new SolidColorBrush(Colors.White, .32 * Math.Clamp(edge / .3, 0, 1)), all);
        }
        if (_time >= S.PinkAt && _time < S.RevealAt)
        {
            double fade = Math.Clamp((_time - S.PinkAt) / .8, 0, 1) * Math.Clamp((S.RevealAt - _time) / 3, 0, 1);
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(255, 65, 165), .13 * fade), all);
            if (_spiral != null)
                using (dc.PushOpacity(.16 * fade))
                {
                    int f = Motion == MotionLevel.Off ? 0 : (int)(_time / _spiral.Delay) % _spiral.Frames.Count;
                    double side = Math.Max(W, H);
                    dc.DrawImage(_spiral.Frames[f], new Rect(W / 2 - side / 2, H / 2 - side / 2, side, side));
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
                if (a.Text != null) DrawWord(dc, a.Text, a.X, a.Y);
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
        double sub = _time - _subAt;
        if (sub >= 0 && sub < .35)
            using (dc.PushOpacity(.7 * Math.Clamp(Math.Min(sub / .07, (.35 - sub) / .18), 0, 1)))
                DrawWord(dc, Loc.Get("first_show_word1"), W / 2, H / 2, Math.Min(1, W / 1500) * 120);
        foreach (var s in _sparks)
        {
            double life = Math.Clamp(1 - (_time - s.Born) / s.Life, 0, 1);
            if (s.Ring) dc.DrawEllipse(null, new Pen(new SolidColorBrush(s.Color, life), 3), new Point(s.X, s.Y), s.Radius * (1 - life), s.Radius * (1 - life));
            else dc.DrawEllipse(new SolidColorBrush(s.Color, life), null, new Point(s.X, s.Y), s.Size, s.Size);
        }
    }
    private void DrawWord(DrawingContext dc, string text, double x, double y, double size = 0)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Arial", FontStyle.Normal, FontWeight.Bold), size > 0 ? size : Math.Min(56, W * .035), Brushes.White);
        var geometry = ft.BuildGeometry(new Point(x - ft.Width / 2, y - ft.Height / 2));
        if (geometry == null) return;
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Colors.HotPink, .86), 6), geometry);   // outline under the fill, as WPF
        dc.DrawGeometry(Brushes.White, null, geometry);
    }
    private static double Fraction(double v) => v - Math.Floor(v);
    private static double Triangle(double v) => 1 - Math.Abs(Fraction(v) * 2 - 1);
    private static void Free(IEnumerable<Media> media) { foreach (var m in media) foreach (var f in m.Frames) f.Dispose(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _actors.Clear(); _sparks.Clear(); Free(_media); _media.Clear();
        if (_spiral != null) Free(new[] { _spiral }); _spiral = null;
        InvalidateVisual();
    }
}
