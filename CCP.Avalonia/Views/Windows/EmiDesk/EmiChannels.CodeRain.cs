using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    // WPF Services/EmiDesk/EmiChannels.Screensavers.cs:101-268: one moving text block per column.
    private sealed class CodeRainPainter
    {
        private const string Glyphs = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789\u30A2\u30AB\u30B5\u30BF\u30CA\u30CF\u30DE\u30E4\u30E9\u30EF";
        private readonly Random _rng;
        private readonly double _cell, _rows;
        private readonly List<TextBlock> _columns = new();
        private readonly List<double> _y = new(), _speed = new();
        private readonly List<int> _tails = new();
        private double _last;
        private int _churn;

        internal CodeRainPainter(Canvas host, Random rng)
        {
            _rng = rng;
            double k = Math.Max(.1, host.Width / 152);
            _cell = Math.Max(5, 6 * k);
            double step = Math.Max(3, 4 * k);
            _rows = Math.Ceiling(host.Height / _cell) + 2;
            AddGlassBox(host, 0, 0, host.Width, host.Height, "#000000", 1);
            int count = Math.Max(1, (int)Math.Ceiling(host.Width / step));
            for (int i = 0; i < count; i++)
            {
                int tail = 5 + rng.Next(5);
                var color = Color.Parse(rng.NextDouble() < .05 ? "#FF69B4" : "#00FF41");
                var stops = new GradientStops();
                for (int j = 0; j <= tail; j++)
                {
                    double alpha = j == tail ? 1 : (double)j / tail * .7;
                    var band = Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);
                    stops.Add(new GradientStop(band, (double)j / (tail + 1)));
                    stops.Add(new GradientStop(band, (double)(j + 1) / (tail + 1)));
                }
                var text = new TextBlock
                {
                    Text = Roll(tail), FontFamily = new FontFamily("Cascadia Mono, Consolas, Noto Sans Mono, monospace"),
                    FontSize = _cell, LineHeight = _cell, TextAlignment = TextAlignment.Center, Width = _cell * 2,
                    Foreground = new LinearGradientBrush { StartPoint = RelativePoint.TopLeft, EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = stops }
                };
                Canvas.SetLeft(text, i * step - _cell * .5);
                host.Children.Add(text);
                _columns.Add(text); _tails.Add(tail);
                _y.Add(-Math.Floor(rng.NextDouble() * 40));
                _speed.Add(12 + rng.NextDouble() * 26);
            }
            for (double y = 0; y < host.Height; y += 2) AddGlassBox(host, 0, y, host.Width, 1, "#000000", .14);
        }

        internal void Tick(double ms)
        {
            double dt = Math.Clamp(ms - _last, 0, 120) / 1000;
            _last = ms;
            for (int i = 0; i < _columns.Count; i++)
            {
                double y = _y[i] + _speed[i] * dt;
                if (y - _tails[i] > _rows) { y = -2; _columns[i].Text = Roll(_tails[i]); }
                _y[i] = y;
                Canvas.SetTop(_columns[i], (Math.Floor(y) - _tails[i]) * _cell);
            }
            if (_columns.Count > 0)
            {
                _churn = (_churn + 1) % _columns.Count;
                _columns[_churn].Text = Roll(_tails[_churn]);
            }
        }

        private string Roll(int tail)
        {
            var glyphs = new char[tail * 2 + 1];
            for (int i = 0; i <= tail; i++)
            {
                if (i > 0) glyphs[i * 2 - 1] = '\n';
                glyphs[i * 2] = Glyphs[_rng.Next(Glyphs.Length)];
            }
            return new string(glyphs);
        }
    }
}
