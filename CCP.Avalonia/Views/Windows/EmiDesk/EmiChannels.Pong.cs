using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    // WPF EmiChannels.cs:130-135,162-166. Motion is the channel, so no still is offered.
    internal static bool PongFeasible(MotionLevel level) => level == MotionLevel.Full;

    // WPF EmiChannels.cs:263-397. The original values are measured at a 60 DIP glass.
    internal sealed class PongPainter
    {
        private readonly double _w, _h, _k, _paddleH, _paddleW, _speed, _ballSize, _inset;
        private readonly Random _rng;
        private readonly Rectangle _padL = new(), _padR = new(), _ball = new();
        private readonly TextBlock _scoreL = new(), _scoreR = new();
        private double _x, _y, _vx, _vy, _pl, _pr, _last;
        private int _left, _right;
        private readonly int _missSide;
        private readonly double _missAtMs;
        internal double BallX => _x;
        internal int Points => _left + _right;

        internal PongPainter(double w, double h, Random rng)
        {
            _w = w; _h = h; _rng = rng;
            _k = Math.Max(.5, w / 60);
            _paddleH = 10 * _k; _paddleW = Math.Max(1, 2 * _k);
            _speed = 55 * _k; _ballSize = Math.Max(1.5, 3 * _k); _inset = 6 * _k;
            _missSide = rng.Next(2); _missAtMs = 3200 + rng.NextDouble() * 4200;
            _x = w / 2; _y = h / 2;
            _vx = _speed * (rng.Next(2) == 0 ? 1 : -1);
            _vy = _speed * (rng.NextDouble() * .9 - .45);
            _pl = _pr = h / 2;
        }

        internal void Attach(Canvas host)
        {
            AddGlassBox(host, 0, 0, _w, _h, "#0E0E1C", 1);
            double dash = Math.Max(2, 4 * _k);
            for (double y = 2 * _k; y < _h; y += dash * 2)
                AddGlassBox(host, Math.Round(_w / 2), y, Math.Max(1, _k), dash, "#FF69B4", .45);
            foreach (var pad in new[] { _padL, _padR })
            {
                pad.Width = _paddleW; pad.Height = _paddleH; pad.Fill = Brush.Parse("#FF69B4");
                host.Children.Add(pad);
            }
            Canvas.SetLeft(_padL, _inset); Canvas.SetLeft(_padR, _w - _inset - _paddleW);
            _ball.Width = _ball.Height = _ballSize; _ball.Fill = Brush.Parse("#FF69B4"); host.Children.Add(_ball);
            foreach (var (score, x) in new[] { (_scoreL, _w / 2 - 14 * _k), (_scoreR, _w / 2 + 5 * _k) })
            {
                score.Text = "0"; score.FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
                score.FontSize = Math.Max(5, _h * .14); score.Foreground = Brush.Parse("#FF69B4"); score.Opacity = .75;
                Canvas.SetLeft(score, x); Canvas.SetTop(score, 2 * _k); host.Children.Add(score);
            }
            Place();
        }

        internal void Tick(double tMs)
        {
            double dt = Math.Clamp(tMs - _last, 0, 80) / 1000; _last = tMs;
            _x += _vx * dt; _y += _vy * dt;
            if (_y < _k) { _y = _k; _vy = Math.Abs(_vy); }
            if (_y > _h - 4 * _k) { _y = _h - 4 * _k; _vy = -Math.Abs(_vy); }
            double left = _inset, right = _w - _inset - _paddleW;
            _pl = Track(_pl, tMs > _missAtMs && _missSide == 0 ? .02 : .16);
            _pr = Track(_pr, tMs > _missAtMs && _missSide == 1 ? .02 : .16);
            _pl = Math.Clamp(_pl, _paddleH / 2, _h - _paddleH / 2);
            _pr = Math.Clamp(_pr, _paddleH / 2, _h - _paddleH / 2);
            if (_vx < 0 && _x <= left + _paddleW && Math.Abs(_y - _pl) < _paddleH / 2 + 2 * _k)
            { _x = left + _paddleW; _vx = Math.Abs(_vx); _vy += (_y - _pl) * 1.6; }
            if (_vx > 0 && _x + _ballSize >= right && Math.Abs(_y - _pr) < _paddleH / 2 + 2 * _k)
            { _x = right - _ballSize; _vx = -Math.Abs(_vx); _vy += (_y - _pr) * 1.6; }
            if (_x < -4 * _k) { _scoreR.Text = (++_right).ToString(System.Globalization.CultureInfo.InvariantCulture); Serve(1); }
            if (_x > _w + 4 * _k) { _scoreL.Text = (++_left).ToString(System.Globalization.CultureInfo.InvariantCulture); Serve(-1); }
            Place();
        }
        private double Track(double cur, double gain) => cur + (_y + 1.5 * _k - cur) * gain;
        private void Serve(int dir) { _x = _w / 2; _y = _h / 2; _vx = _speed * dir; _vy = _speed * (_rng.NextDouble() * .9 - .45); }
        private void Place()
        {
            Canvas.SetTop(_padL, _pl - _paddleH / 2); Canvas.SetTop(_padR, _pr - _paddleH / 2);
            Canvas.SetLeft(_ball, _x); Canvas.SetTop(_ball, _y);
        }
    }
}
