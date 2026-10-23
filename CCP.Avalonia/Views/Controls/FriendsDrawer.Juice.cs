// ADAPTED from WPF FriendsDrawer.Juice.cs and MotionFx at 4f75aed68cb06c56af5dc60472ca1ce0d2463000.
// One shared frame source, no infinite Avalonia Animation and no animated Effect.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private static readonly HashSet<FriendsDrawer> Moving = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false, Tag = "friends-fx" };
    private readonly List<Control> _motionRows = new(), _breathing = new();
    private readonly Dictionary<string, Control> _avatars = new(), _rows = new();
    private readonly List<Move> _moves = new();
    private readonly Stopwatch _juiceWatch = new();
    private FrameClock? _juiceClock;
    private double _juiceTime, _lastTick, _breathStart;
    private bool _juiceParked;
    private FriendsSnapshot _last = FriendsSnapshot.Empty;
    private sealed record Move(Control Target, double Start, double Duration, Action<double> Sample, Action? Done);
    private static double Amount => AmbientFxCanvas.Env.Level switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };
    internal bool JuiceRunning => _juiceClock?.IsEnabled == true;

    private void OpenJuice()
    {
        _juiceParked = false;
        Moving.Add(this);
        AmbientFxCanvas.Env.MotionGateChanged -= MotionChanged;
        AmbientFxCanvas.Env.MotionGateChanged += MotionChanged;
    }

    private void CloseJuice()
    {
        Moving.Remove(this);
        AmbientFxCanvas.Env.MotionGateChanged -= MotionChanged;
        StopJuice();
    }

    internal static void StopAllJuice()
    {
        foreach (var drawer in Moving) { drawer._juiceParked = true; drawer.StopJuice(); }
    }

    private void MotionChanged()
    {
        StopJuice();
        StartAmbient();
    }

    private void StopJuice()
    {
        foreach (var move in _moves) { move.Sample(1); move.Done?.Invoke(); }
        _moves.Clear();
        foreach (var dot in _breathing) dot.Opacity = 1;
        _breathing.Clear();
        _fx.Children.Clear();
        _juiceClock?.Stop();
        _juiceWatch.Reset();
        _lastTick = 0;
    }

    private void EnsureJuiceClock()
    {
        if (_moves.Count == 0 && _breathing.Count == 0)
        { _juiceClock?.Stop(); _juiceWatch.Reset(); _lastTick = 0; return; }
        if (_juiceClock == null)
        {
            _juiceClock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
            _juiceClock.Tick += (_, _) =>
            {
                double now = _juiceWatch.Elapsed.TotalMilliseconds;
                double delta = Math.Max(0, now - _lastTick);
                _lastTick = now;
                AdvanceJuice(TimeSpan.FromMilliseconds(delta));
            };
        }
        _juiceWatch.Start();
        _juiceClock.Start();
    }

    // The same step is used by the frame clock and deterministic headless tests.
    internal void AdvanceJuice(TimeSpan delta)
    {
        _juiceTime += Math.Max(0, delta.TotalMilliseconds);
        for (int i = _moves.Count - 1; i >= 0; i--)
        {
            var move = _moves[i];
            double p = Math.Clamp((_juiceTime - move.Start) / move.Duration, 0, 1);
            move.Sample(p);
            if (p >= 1) { _moves.RemoveAt(i); move.Done?.Invoke(); }
        }
        // WPF GlowBreath: 1.8 seconds each way, sine EaseInOut, Full only.
        double opacity = 0.45 + 0.55 * (1 - Math.Cos((_juiceTime - _breathStart) * Math.PI / 1800)) / 2;
        foreach (var dot in _breathing) dot.Opacity = opacity;
        EnsureJuiceClock();
    }

    private void Tween(Control target, double duration, Action<double> sample, double delay = 0, Action? done = null)
    {
        if (!_isOpen || _juiceParked || Amount <= 0) return;
        for (int i = _moves.Count - 1; i >= 0; i--)
            if (ReferenceEquals(_moves[i].Target, target))
            { _moves[i].Sample(1); _moves[i].Done?.Invoke(); _moves.RemoveAt(i); }
        sample(0);
        _moves.Add(new Move(target, _juiceTime + delay, duration, sample, done));
        EnsureJuiceClock();
    }

    private void PlayEntrance()
    {
        double k = Amount;
        if (k <= 0) { Opacity = 1; RenderTransform = null; return; }
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative); // WPF bottom left
        RenderTransform = new TransformGroup { Children = { scale, slide } };
        double ms = 220 / Math.Max(k, 0.5) * (k < 1 ? 0.8 : 1);
        Tween(this, ms, p =>
        {
            double e = BackOut(p, 0.45 * k);
            scale.ScaleX = scale.ScaleY = 1 - 0.06 * k * (1 - e);
            slide.Y = 14 * k * (1 - e);
            Opacity = Math.Clamp(p * ms / 160, 0, 1);
        });
        for (int i = 0; i < _motionRows.Count; i++) Stagger(_motionRows[i], 40 * Math.Min(i, 6));
    }

    private void Stagger(Control row, double delay = 0)
    {
        if (Amount <= 0) return;
        var slide = new TranslateTransform();
        row.RenderTransform = slide;
        Tween(row, 260, p =>
        {
            slide.Y = 10 * (1 - new QuadraticEaseOut().Ease(p));
            row.Opacity = Math.Clamp(new QuadraticEaseOut().Ease(Math.Min(1, p * 260 / 220)), 0, 1);
        }, delay);
    }

    private void ResetRows()
    {
        // Retire detached row jobs; drawer entrance and the independent spark layer can finish.
        for (int i = _moves.Count - 1; i >= 0; i--)
            if (_moves[i].Target != this && _moves[i].Target.Parent != _fx)
            { _moves[i].Sample(1); _moves[i].Done?.Invoke(); _moves.RemoveAt(i); }
        foreach (var dot in _breathing) dot.Opacity = 1;
        _breathing.Clear();
        _motionRows.Clear();
        _rows.Clear();
        _avatars.Clear();
        EnsureJuiceClock();
    }

    private void StartAmbient()
    {
        if (!_isOpen || _juiceParked || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
        _breathStart = _juiceTime;
        foreach (var avatar in _avatars.Values)
            if (avatar is Grid grid)
                foreach (var child in grid.Children)
                    if (child is Ellipse { Tag: "friends-dot-on" } dot) { dot.Opacity = 0.45; _breathing.Add(dot); }
        EnsureJuiceClock();
    }

    private Control MotionRow(string id, Control row)
    {
        _motionRows.Add(row);
        _rows[id] = row;
        return row;
    }

    private void AttachSheen(Border row, bool open)
    {
        var rest = row.Background;
        // A sibling overlay keeps the row content intact and clips the travelling band.
        var content = row.Child!;
        row.Child = null;
        var host = new Grid { ClipToBounds = true, Children = { content } };
        row.Child = host;
        row.PointerEntered += (_, _) =>
        {
            if (!open) row.Background = ButtonHover;
            if (Amount <= 0 || !_isOpen || _juiceParked) return;
            var slide = new TranslateTransform(-90, 0);
            var band = new Rectangle
            {
                Width = 70, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
                Opacity = 0.9 * Amount, RenderTransform = slide, Tag = "friends-sheen",
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromArgb(0, 185, 156, 255), 0),
                        new GradientStop(Color.FromArgb(48, 185, 156, 255), 0.5), new GradientStop(Color.FromArgb(0, 185, 156, 255), 1) },
                },
            };
            host.Children.Add(band);
            Tween(band, 520 / Math.Max(Amount, 0.5), p => slide.X = -90 + (Math.Max(row.Bounds.Width, 200) + 110) * new QuadraticEaseInOut().Ease(p),
                done: () => host.Children.Remove(band));
        };
        row.PointerExited += (_, _) => { if (!open) row.Background = rest; };
    }

    private void HopAvatar(string id)
    {
        double k = Amount;
        if (k <= 0 || !_avatars.TryGetValue(id, out var avatar)) return;
        var slide = new TranslateTransform();
        avatar.RenderTransform = slide;
        Tween(avatar, 560, p => slide.Y = p < 170.0 / 560
            ? -10 * k * new QuadraticEaseOut().Ease(p * 560 / 170)
            : -10 * k * (1 - BounceOut((p * 560 - 170) / 390)));
        Sparks(avatar, Mint, 8);
    }

    private void Pop(Control from, IBrush color)
    {
        double k = Amount;
        if (k <= 0 || !_isOpen || _juiceParked) return;
        var scale = new ScaleTransform(1, 1);
        from.RenderTransformOrigin = RelativePoint.Center;
        from.RenderTransform = scale;
        Tween(from, 380, p => scale.ScaleX = scale.ScaleY = p < 110.0 / 380
            ? 1 + 0.22 * k * new QuadraticEaseOut().Ease(p * 380 / 110)
            : 1 + 0.22 * k * (1 - ElasticOut((p * 380 - 110) / 270)));
        Sparks(from, color, (int)Math.Round(12 * k));
    }

    // WPF easings with their authored parameters (Avalonia's counterparts expose no parameters).
    private static double BackOut(double p, double amplitude)
    {
        double v = 1 - p;
        return 1 - v * v * v + amplitude * v * Math.Sin(Math.PI * v);
    }
    private static double ElasticOut(double p)
    {
        double v = 1 - p;
        return 1 - (Math.Exp(4 * v) - 1) / (Math.Exp(4) - 1) * Math.Sin(2.5 * Math.PI * v);
    }
    private static double BounceOut(double p)
    {
        double t = (1 - p) * 8.5; // two bounces, bounciness 3
        double bounce = Math.Floor(Math.Log(2 * t + 1) / Math.Log(3));
        double start = (Math.Pow(3, bounce) - 1) / 2, end = (Math.Pow(3, bounce + 1) - 1) / 2;
        double radius = (end - start) / 2, x = t - (start + end) / 2;
        return 1 - Math.Pow(1.0 / 3, 2 - bounce) * (1 - x * x / (radius * radius));
    }

    private Point? Center(Control from) => from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), _fx);

    private void Sparks(Control from, IBrush color, int count)
    {
        if (!_isOpen || _juiceParked || Amount <= 0 || Center(from) is not { } c) return;
        for (int i = 0; i < count; i++)
        {
            double size = 3 + Random.Shared.NextDouble() * 3;
            // ponytail: sibling BoxShadow in place of WPF's per-particle Effect.
            var dot = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size), Background = color,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 6, Color = ((ISolidColorBrush)color).Color }) };
            Canvas.SetLeft(dot, c.X - size / 2); Canvas.SetTop(dot, c.Y - size / 2);
            var slide = new TranslateTransform();
            dot.RenderTransform = slide;
            _fx.Children.Add(dot);
            double angle = Random.Shared.NextDouble() * Math.PI * 2, distance = (24 + Random.Shared.NextDouble() * 30) * Amount;
            Tween(dot, 420 + Random.Shared.Next(260), p =>
            {
                double e = new QuadraticEaseOut().Ease(p);
                slide.X = Math.Cos(angle) * distance * e; slide.Y = (Math.Sin(angle) * distance + 6) * e;
                dot.Opacity = 1 - p;
            }, done: () => _fx.Children.Remove(dot));
        }
    }

    private void Shockwave(Control from)
    {
        double k = Amount;
        if (!_isOpen || _juiceParked || k <= 0 || Center(from) is not { } c) return;
        for (int i = 0; i < 2; i++)
        {
            var scale = new ScaleTransform(0.4, 0.4);
            var ring = new Ellipse { Width = 20, Height = 20, Stroke = Mint, StrokeThickness = 2.5,
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = scale, Tag = "friends-shockwave", Opacity = 0 };
            Canvas.SetLeft(ring, c.X - 10); Canvas.SetTop(ring, c.Y - 10);
            _fx.Children.Add(ring);
            double delay = i * 120, ms = 620 / Math.Max(k, 0.5), delayStart = _juiceTime + i * 120;
            Tween(ring, ms, p =>
            {
                scale.ScaleX = scale.ScaleY = 0.4 + (0.6 + 9 * k) * new CubicEaseOut().Ease(p);
                ring.Opacity = _juiceTime < delayStart ? 0 : 0.95 * (1 - p);
            }, delay, () => _fx.Children.Remove(ring));
        }
        Sparks(from, Mint, (int)Math.Round(14 * k));
    }
}
