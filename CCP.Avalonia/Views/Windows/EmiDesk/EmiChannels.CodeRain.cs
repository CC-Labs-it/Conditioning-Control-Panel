using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    // WPF EmiChannels.Screensavers.cs:101-273: one falling text column per strip.
    internal sealed class CodeRainPainter
    {
        private const string Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789\u30A2\u30AB\u30B5\u30BF\u30CA\u30CF\u30DE\u30E4\u30E9\u30EF";
        private readonly double _w, _h, _cell, _step, _rows;
        private readonly Random _rng;
        private readonly List<TextBlock> _columns = new();
        private readonly List<double> _y = new(), _speed = new();
        private readonly List<int> _tail = new();
        private double _last;
        private int _churn;

        internal CodeRainPainter(double w, double h, Random rng)
        {
            _w = w; _h = h; _rng = rng;
            double k = Math.Max(.1, w / 152);
            _cell = Math.Max(5, 6 * k); _step = Math.Max(3, 4 * k);
            _rows = Math.Ceiling(h / _cell) + 2;
        }

        internal void Attach(Canvas host)
        {
            AddGlassBox(host, 0, 0, _w, _h, "#000000", 1);
            int count = Math.Max(1, (int)Math.Ceiling(_w / _step));
            for (int i = 0; i < count; i++)
            {
                int tail = 5 + _rng.Next(5);
                var column = new TextBlock
                {
                    Text = Roll(tail), FontFamily = new FontFamily("Cascadia Mono, Consolas, Noto Sans Mono, MS Gothic, Yu Gothic UI, Meiryo, Courier New, monospace"),
                    FontSize = _cell, LineHeight = _cell, TextAlignment = TextAlignment.Center,
                    Width = _cell * 2, Foreground = TailBrush(_rng.NextDouble() < .05 ? Color.Parse("#FF69B4") : Color.Parse("#00FF41"), tail),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(column, i * _step - _cell * .5);
                _columns.Add(column); _tail.Add(tail);
                _y.Add(-Math.Floor(_rng.NextDouble() * 40)); _speed.Add(12 + _rng.NextDouble() * 26);
                host.Children.Add(column);
            }
            for (double y = 0; y < _h; y += 2) AddGlassBox(host, 0, y, _w, 1, "#000000", .14);
        }

        internal void Tick(double ms)
        {
            double dt = Math.Clamp(ms - _last, 0, 120) / 1000; _last = ms;
            for (int i = 0; i < _columns.Count; i++)
            {
                double y = _y[i] + _speed[i] * dt;
                if (y - _tail[i] > _rows) { y = -2; _columns[i].Text = Roll(_tail[i]); }
                _y[i] = y;
                Canvas.SetTop(_columns[i], (Math.Floor(y) - _tail[i]) * _cell);
            }
            if (_columns.Count > 0)
            {
                _churn = (_churn + 1) % _columns.Count;
                _columns[_churn].Text = Roll(_tail[_churn]);
            }
        }

        private string Roll(int tail)
        {
            var chars = new char[(tail + 1) * 2 - 1];
            for (int i = 0; i <= tail; i++)
            {
                if (i > 0) chars[i * 2 - 1] = '\n';
                chars[i * 2] = Glyphs[_rng.Next(Glyphs.Length)];
            }
            return new string(chars);
        }

        private static LinearGradientBrush TailBrush(Color color, int tail)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            int n = tail + 1;
            for (int j = 0; j < n; j++)
            {
                int k = n - 1 - j;
                double alpha = k == 0 ? 1 : Math.Max(0, 1 - (double)k / tail) * .7;
                var band = Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);
                brush.GradientStops.Add(new GradientStop(band, (double)j / n));
                brush.GradientStops.Add(new GradientStop(band, (double)(j + 1) / n));
            }
            return brush;
        }
    }
}
