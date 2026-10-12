using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowEffects.cs: the show's private, disposable
/// effect layer; never registers a session or writes a setting. Timeline, slots and gather spiral (Tick :106, Spawn
/// :167, Animate :211) are WPF's, timings from <see cref="S"/>. WPF stepped the production compositor layers, none of
/// which exist on this head, so <see cref="Render"/> draws with plain calls (no Effect: P82). Here: pictures, bubbles,
/// pink tint, gather, cues. ponytail (carried to first-show-c): sparks/particles, subliminal flash, brain drain,
/// spiral and the floating words.</summary>
internal sealed class FirstShowEffects : Control, IDisposable
{
    private sealed record Media(List<Bitmap> Frames, double Delay);
    private sealed class Actor
    {
        public int Id, MediaIndex = -1, Slot = -1, Frame;
        public double Born, Life, X, Y, W, H, Angle, Alpha, Scale = 1, HomeX, HomeY, GatherX, GatherY;
        public bool Gathering, Gone, Bubble; public Media? Media;
    }
    private static readonly double[] Xs = { .15, .82, .25, .74, .13, .86, .42, .60 };
    private static readonly double[] Ys = { .25, .66, .76, .23, .51, .42, .20, .79 };
    private readonly List<Media> _media = new();
    private readonly List<Actor> _actors = new();
    private readonly Action<string> _cue;
    private FirstShowDeck? _deck;
    private double _time, _nextFlash = S.FlashAt, _nextBubble = S.BubblesAt;
    private int _serial;
    private bool _disposed, _revealed;
    public FirstShowEffects(Action<string> cue) { _cue = cue; IsHitTestVisible = false; }
    internal FirstShowEffects() : this(_ => { }) { }   // --render-all: an empty layer (no deck loaded)
    private double W => Math.Max(1, double.IsNaN(Width) ? Bounds.Width : Width);
    private double H => Math.Max(1, double.IsNaN(Height) ? Bounds.Height : Height);
    private static MotionLevel Motion => CoreSettings.Current?.MotionLevel ?? MotionLevel.Full;
    public async Task LoadAsync(IReadOnlyList<string> paths)
    {
        var loaded = await Task.Run(() => paths.Take(FirstShowMediaLoader.TargetPictures).Select(Decode).OfType<Media>().ToList());
        if (_disposed) { Free(loaded); return; }
        if (loaded.Count == 0) throw new InvalidOperationException("No preview images could be decoded.");
        _media.AddRange(loaded); _deck = new FirstShowDeck(_media.Count);
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
        bool moving = Motion != MotionLevel.Off;
        foreach (var a in _actors) if (!a.Gone) Animate(a, seconds, moving);
        _actors.RemoveAll(a => a.Gone);
        if (seconds >= S.RevealAt && !_revealed) { _revealed = true; _cue("reveal"); }
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
        _cue(bubble ? "bubbles" : "flash");
    }
    private void Animate(Actor a, double time, bool moving)
    {
        double age = time - a.Born;
        if (age < 0) { a.Alpha = 0; return; }
        double motion = Motion == MotionLevel.Full ? 1 : moving ? .4 : 0;
        a.X = a.HomeX + Math.Sin(age * (.9 + a.Id % 4 * .19) + a.Id) * W * .023 * motion;
        a.Y = a.HomeY + Math.Cos(age * (1.1 + a.Id % 3 * .17) + a.Id * .7) * H * .025 * motion;
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
            if (p >= 1) a.Gone = true;
        }
        a.Frame = a.Media == null || !moving ? 0 : (int)(age / a.Media.Delay) % a.Media.Frames.Count;
    }
    /// <summary>WPF Pop (:310): a click on a live bubble pops it.</summary>
    public void Pop(double x, double y)
    {
        if (_disposed || _time >= S.GatherAt) return;
        var a = _actors.LastOrDefault(a => a.Bubble && !a.Gone && Math.Pow(a.X - x, 2) + Math.Pow(a.Y - y, 2) < Math.Pow(a.W * .52, 2));
        if (a == null) return;
        a.Gone = true;
        _cue("pop");
    }
    public override void Render(DrawingContext dc)
    {
        if (_disposed) return;
        var all = new Rect(0, 0, W, H);
        if (_time >= S.PinkAt && _time < S.RevealAt)
        {
            double fade = Math.Clamp((_time - S.PinkAt) / .8, 0, 1) * Math.Clamp((S.RevealAt - _time) / 3, 0, 1);
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(255, 65, 165), .13 * fade), all);
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
                if (a.Bubble)
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
    }
    private static double Fraction(double v) => v - Math.Floor(v);
    private static void Free(IEnumerable<Media> media) { foreach (var m in media) foreach (var f in m.Frames) f.Dispose(); }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _actors.Clear(); Free(_media); _media.Clear();
        InvalidateVisual();
    }
}
