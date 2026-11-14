using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiChannels.Wrong.cs:31-395. The window owns and clears the canvas.
internal sealed class WrongPainter
{
    private readonly double _w, _h, _k;
    private readonly Random _rng;
    private readonly bool _negative;
    private readonly double _drop0, _drop1, _length0, _length1;
    private readonly List<Rectangle> _snow = new(), _topSnow = new();
    private readonly Rectangle _roll = new(), _dead = new(), _tear = new(), _flash = new();
    private readonly TextBlock _face = new(), _ghost = new(), _word = new();
    private double _faceX, _faceY, _rollH, _lastScramble = double.NegativeInfinity;
    private bool _ended;
    private static readonly IBrush Screen = Brush.Parse("#0E0E1C"), Cream = Brush.Parse("#F5F0E1");
    private static readonly IBrush GrainLight = Brush.Parse("#C9C9D6"), GrainDark = Brush.Parse("#4A4A58");
    private static readonly IBrush TopLight = Brush.Parse("#80FFFFFF"), TopDark = Brush.Parse("#80000000");

    internal WrongPainter(double w, double h, Random? rng = null, bool? negative = null)
    {
        _w = w; _h = h; _k = Math.Max(.5, w / 152);
        _rng = rng ?? new Random();
        _negative = negative ?? _rng.NextDouble() > .5;
        _drop0 = 240 + _rng.NextDouble() * 180; _drop1 = 620 + _rng.NextDouble() * 220;
        _length0 = 50 + _rng.NextDouble() * 30; _length1 = 50 + _rng.NextDouble() * 30;
    }

    internal void Attach(Canvas host)
    {
        Box(host, _w, _h, Screen);
        if (_negative)
        {
            Box(host, _w, _h, Brush.Parse("#FF69B4"));
            Face(host, _ghost, Math.Max(5, 22 * _k), .55, 3 * _k, -6 * _k);
            Face(host, _face, Math.Max(6, 30 * _k), 1, 0, 0);
            _faceX = Canvas.GetLeft(_face); _faceY = Canvas.GetTop(_face);
        }
        else
        {
            Snow(host, _snow, 132, GrainLight, GrainDark);
            string[] words = { "soon", "hi", "again" };
            _word.Text = words[_rng.Next(words.Length)];
            _word.FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
            _word.FontSize = Math.Max(5, 14 * _k); _word.Foreground = Brush.Parse("#E8E8F2");
            _word.Opacity = 0; host.Children.Add(_word); Centre(_word, 0, -6 * _k);
            Snow(host, _topSnow, 29, TopLight, TopDark);
        }
        _rollH = Math.Max(2, _h * .16);
        _roll.Width = _w; _roll.Height = _rollH;
        _roll.Fill = new LinearGradientBrush
        {
            StartPoint = new global::Avalonia.RelativePoint(0, 0, global::Avalonia.RelativeUnit.Relative),
            EndPoint = new global::Avalonia.RelativePoint(0, 1, global::Avalonia.RelativeUnit.Relative),
            GradientStops = new GradientStops { new(Color.Parse("#00FFFFFF"), 0), new(Color.Parse("#2EFFFFFF"), .5), new(Color.Parse("#00FFFFFF"), 1) }
        };
        host.Children.Add(_roll);
        Layer(host, _dead, Screen, 0); Layer(host, _tear, Cream, 0); Layer(host, _flash, Cream, 1);
    }

    internal void Tick(double ms)
    {
        if (ms >= 1150)
        {
            if (_ended) return;
            _ended = true; _dead.Opacity = 1; _tear.Opacity = 0; _flash.Opacity = 0;
            return;
        }
        _flash.Opacity = ms < 90 ? 1 - ms / 90 : 0;
        if (ms < 950)
        {
            _dead.Opacity = (ms >= _drop0 && ms < _drop0 + _length0) || (ms >= _drop1 && ms < _drop1 + _length1) ? 1 : 0;
            Canvas.SetTop(_roll, (ms % 700) / 700 * (_h + _rollH) - _rollH);
            if (ms - _lastScramble >= 66) { _lastScramble = ms; Scramble(); }
            if (!_negative) _word.Opacity = .35 * Ramp(ms);
            return;
        }
        double p = Math.Clamp((ms - 950) / 200, 0, 1);
        _dead.Opacity = p;
        if (p < .6)
        {
            _tear.Width = _w; _tear.Height = _h + (Math.Max(1, _k) - _h) * p / .6;
            Canvas.SetLeft(_tear, 0); Canvas.SetTop(_tear, (_h - _tear.Height) / 2); _tear.Opacity = .9;
        }
        else
        {
            double r = (p - .6) / .4;
            _tear.Width = Math.Max(.5, _w * (1 - r));
            Canvas.SetLeft(_tear, (_w - _tear.Width) / 2); _tear.Opacity = .9 * (1 - r);
        }
    }

    private static double Ramp(double t) => t <= 300 || t >= 700 ? 0 : t < 380 ? (t - 300) / 80 : t <= 620 ? 1 : (700 - t) / 80;
    private void Scramble()
    {
        if (_negative)
        {
            Canvas.SetLeft(_face, _faceX + (_rng.NextDouble() - .5) * 2 * _k);
            Canvas.SetTop(_face, _faceY + (_rng.NextDouble() - .5) * 2 * _k);
            _ghost.Opacity = .45 + _rng.NextDouble() * .18;
        }
        else { Move(_snow, GrainLight, GrainDark); Move(_topSnow, TopLight, TopDark); }
    }
    private void Move(List<Rectangle> grains, IBrush light, IBrush dark)
    {
        foreach (var grain in grains)
        {
            Canvas.SetLeft(grain, Math.Floor(_rng.NextDouble() * _w)); Canvas.SetTop(grain, Math.Floor(_rng.NextDouble() * _h));
            grain.Fill = _rng.NextDouble() > .5 ? light : dark;
        }
    }
    private void Snow(Canvas host, List<Rectangle> grains, int count, IBrush light, IBrush dark)
    {
        for (int i = 0; i < count; i++) grains.Add(Box(host, Math.Max(.5, (1 + _rng.Next(4)) * _k), Math.Max(.5, _k), i % 2 == 0 ? light : dark));
    }
    private void Face(Canvas host, TextBlock text, double size, double opacity, double dx, double dy)
    {
        text.Text = "0_0"; text.FontFamily = EmiFace.FaceFont; text.FontSize = size; text.Foreground = Screen; text.Opacity = opacity;
        host.Children.Add(text); Centre(text, dx, dy);
    }
    private void Centre(TextBlock text, double dx, double dy)
    {
        text.Measure(new global::Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(text, (_w - text.DesiredSize.Width) / 2 + dx); Canvas.SetTop(text, (_h - text.DesiredSize.Height) / 2 + dy);
    }
    private static Rectangle Box(Canvas host, double w, double h, IBrush fill)
    {
        var box = new Rectangle { Width = w, Height = h, Fill = fill, IsHitTestVisible = false }; host.Children.Add(box); return box;
    }
    private void Layer(Canvas host, Rectangle box, IBrush fill, double opacity)
    {
        box.Width = _w; box.Height = _h; box.Fill = fill; box.Opacity = opacity; box.IsHitTestVisible = false; host.Children.Add(box);
    }
}
