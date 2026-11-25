using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    // WPF Services/EmiDesk/EmiChannels.cs:406-460: five rotating pink arms on dark glass.
    internal sealed class SpiralPainter(double w, double h)
    {
        private readonly Path _path = new()
        {
            Stroke = Brush.Parse("#FF69B4"), StrokeThickness = Math.Max(1.5, w * .032),
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
        };
        internal void Attach(Canvas host)
        {
            host.Background = Brush.Parse("#0E0E1C");
            host.Children.Add(_path);
        }
        internal void Tick(double ms)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (int arm = 0; arm < 5; arm++)
                {
                    double phase = ms / 400 + arm * Math.PI * 2 / 5;
                    for (double a = 0; a < 9; a += .22)
                    {
                        double r = a / 9 * Math.Max(w, h) * .72;
                        var point = new Point(w / 2 + Math.Cos(a + phase) * r, h / 2 + Math.Sin(a + phase) * r);
                        if (a == 0) context.BeginFigure(point, false);
                        else context.LineTo(point);
                    }
                    context.EndFigure(false);
                }
            }
            _path.Data = geometry;
        }
    }
}
