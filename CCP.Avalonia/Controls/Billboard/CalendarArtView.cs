using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Billboard;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

/// <summary>WPF BuiltInArt.CalendarArtView: sparse counted days, lifted today and progress bar.
/// Uses the shell's shared beat; no Effects, bitmap ownership or OS-specific APIs.</summary>
public sealed class CalendarArtView : Control, IBillboardArtView
{
    private static readonly HashSet<CalendarArtView> Active = new();
    private readonly BillboardCalendarInfo _data;
    private BeatLoop? _loop;
    private double _seconds = 1.6, _offset;
    private bool _released;
    // Cached so a 30 fps tick allocates no brushes and no text layouts (WPF caches its landed cells layer).
    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Color.FromRgb(0x14, 0x0e, 0x20));
    private static readonly IBrush Sheet = new ImmutableSolidColorBrush(Color.FromRgb(0x1e, 0x16, 0x30));
    private static readonly IBrush Unlit = new ImmutableSolidColorBrush(Color.FromRgb(46, 42, 82));
    private static readonly IBrush UnlitToday = new ImmutableSolidColorBrush(Color.FromRgb(58, 52, 96));
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromRgb(16, 14, 30));
    private static readonly IBrush Shadow = new ImmutableSolidColorBrush(Colors.Black, .35);
    private static readonly IBrush LitInk = new ImmutableSolidColorBrush(Color.FromRgb(26, 11, 24));
    private static readonly IBrush UnlitInk = new ImmutableSolidColorBrush(Color.FromRgb(180, 176, 220));
    private static readonly IPen PressedEdge = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Black, .12), 1);
    private static readonly IPen RaisedEdge = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, .12), 1);
    private static readonly Typeface NumberFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private readonly Dictionary<(int Day, bool Lit), FormattedText> _numbers = new();
    private double _numbersCell;
    private Color _accentFor;
    private IBrush? _accent, _accentLit, _accentToday;
    internal int TextLayoutsBuilt { get; private set; }
    internal double Offset => _offset;
    internal static Func<bool> MotionAllowed = () => Env.AllowAmbientLoops;
    internal bool Running => _loop != null;
    internal BillboardCalendarInfo Data => _data;
    public Color Accent { get; set; } = Color.FromRgb(0xff, 0x4f, 0xa8);

    public CalendarArtView(BillboardCalendarInfo data)
    {
        _data = data;
        IsHitTestVisible = false;
        DetachedFromVisualTree += (_, _) => Hold();
    }

    public void Run()
    {
        if (_released) return;
        if (!MotionAllowed()) { Hold(); return; }
        if (_loop != null) return;
        Active.Add(this);
        _loop = new BeatLoop(this, elapsed =>
        {
            _seconds = _offset + elapsed;
            InvalidateVisual();
        });
        _loop.Start();
    }

    public void Hold()
    {
        // Like WPF Pause: only a running clock moves the offset, so a hold before the first Run keeps the entrance.
        if (_loop != null) _offset = _seconds;
        _loop?.Stop();
        _loop = null;
        Active.Remove(this);
    }

    public void Release()
    {
        Hold();
        _released = true;
    }

    // WPF leaves billboard decoration alone on panic. This head parks its loop; the next Run (tab return,
    // card change) resumes it, so a single-card deck is never frozen for the rest of the session.
    internal static void StopForPanic()
    {
        foreach (var art in new List<CalendarArtView>(Active)) art.Hold();
    }

    private FormattedText Number(int day, bool lit, double cell)
    {
        if (cell != _numbersCell) { _numbers.Clear(); _numbersCell = cell; }
        if (!_numbers.TryGetValue((day, lit), out var text))
        {
            text = new FormattedText(day.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, NumberFace, cell * .32, lit ? LitInk : UnlitInk);
            _numbers[(day, lit)] = text;
            TextLayoutsBuilt++;
        }
        return text;
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        if (_released) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (_accent == null || _accentFor != Accent)
        {
            _accentFor = Accent;
            _accent = new ImmutableSolidColorBrush(Accent);
            _accentLit = new ImmutableSolidColorBrush(Accent, .88);
            _accentToday = new ImmutableSolidColorBrush(Accent, .95);
        }
        dc.DrawRectangle(Ground, null, new Rect(Bounds.Size));
        const int cols = 7;
        double cell = h * .115, pitch = cell * 1.08, gx = w * .5, gy = h * .14;
        int rows = (_data.Days + cols - 1) / cols;
        double pad = cell * .32, bob = Math.Sin(_seconds) * h * .005;
        using var shift = dc.PushTransform(Matrix.CreateTranslation(0, bob));
        Plate(dc, new Rect(gx - pad, gy - pad, cols * pitch - cell * .08 + pad * 2,
            rows * pitch - cell * .08 + pad * 2 + (_data.Needed > 0 ? cell * .62 : 0)),
            cell * .28, Sheet, h * .02, false);
        for (int day = 1; day <= _data.Days; day++)
        {
            bool today = day == _data.Today, lit = _data.IsLit(day);
            int i = day - 1;
            double lift = today ? cell * (.08 + Math.Max(0, Math.Sin(_seconds * 2)) * .06) : 0;
            var rect = new Rect(gx + i % cols * pitch, gy + i / cols * pitch - lift, cell, cell);
            // ponytail: day pop and depth only; ground/vignette, motes, twinkles, glint and bar gradient/sheen/spark are missing (parity row host-lock-calendar-art).
            double pop = lit && !today ? BackOut(_seconds * 2.4 - i * .03) : 1;
            if (pop <= .01) lit = false;
            double scale = pop > .01 ? pop : 1;
            using var zoom = dc.PushTransform(Matrix.CreateTranslation(-rect.Center.X, -rect.Center.Y)
                * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(rect.Center.X, rect.Center.Y));
            var fill = lit ? (today ? _accentToday! : _accentLit!) : today ? UnlitToday : Unlit;
            Plate(dc, rect, cell * .2, fill, today ? lift + cell * .06 : lit ? 0 : cell * .06, lit && !today);
            if (today)
                dc.DrawRectangle(null, new Pen(new SolidColorBrush(Colors.White,
                    Math.Clamp(.6 + .4 * Math.Sin(_seconds * 4), 0, 1)), cell * .07), rect, cell * .2, cell * .2);
            var text = Number(day, lit, cell);
            dc.DrawText(text, new Point(rect.X + (cell - text.Width) / 2, rect.Y + (cell - text.Height) / 2));
        }
        if (_data.Needed > 0)
        {
            double bw = cols * pitch - cell * .08, bh = cell * .26, y = gy + rows * pitch + cell * .12;
            var track = new Rect(gx, y, bw, bh);
            Plate(dc, track, bh / 2, Track, 0, true);
            double share = Math.Clamp(_data.Counted / (double)_data.Needed, 0, 1)
                * (1 - Math.Pow(1 - Math.Clamp(_seconds * 1.5, 0, 1), 3));
            if (share > 0)
                dc.DrawRectangle(_accent, null, new Rect(gx, y, Math.Max(bh, bw * share), bh), bh / 2, bh / 2);
        }
    }

    private static double BackOut(double t)
    {
        t = Math.Clamp(t, 0, 1) - 1;
        return 1 + 3 * t * t * t + 2 * t * t;
    }

    private static void Plate(DrawingContext dc, Rect rect, double radius, IBrush fill, double lift, bool pressed)
    {
        if (lift > 0)
            dc.DrawRectangle(Shadow, null, rect.Translate(new Vector(0, lift)), radius, radius);
        dc.DrawRectangle(fill, pressed ? PressedEdge : RaisedEdge, rect, radius, radius);
    }
}
