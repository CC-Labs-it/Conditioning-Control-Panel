using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiChannels.Shop.cs:34-355. A broadcast, not a storefront.
internal sealed class ShopPainter
{
    private readonly Canvas _host;
    private readonly Rectangle _body, _foot;
    private readonly TextBlock _price, _call;
    private readonly double _cx, _bodyWidth;
    private readonly int _start, _drop;
    private static readonly double[] Squeeze = { 1, .6, .15, .6 };
    private static readonly IBrush Gold = Brush.Parse("#F2C14E"), Coral = Brush.Parse("#E8836F");

    internal ShopPainter(Canvas host, int item)
    {
        _host = host;
        double w = host.Width, h = host.Height, k = Math.Max(.35, w / 152);
        string[] names = { "CARD", "WATCH", "DISC" };
        int[] starts = { 480, 1290, 760 };
        string[] colors = { "#F5F0E1", "#F2C14E", "#FF69B4" };
        item = Math.Clamp(item, 0, 2);
        _start = starts[item];
        _drop = Math.Max(7, (int)Math.Ceiling((_start - 9) / 81.0));
        double title = Pixel(7 * k, w / 13.5, 4), call = Pixel(7 * k, w / 8.5, 4);
        double label = Pixel(7 * k, w / 6, 4), price = Pixel(12 * k, w / 7.5, 6);
        double headH = Math.Max(title * 1.6, 16 * k), footH = Math.Max(call * 1.6, 14 * k);
        double priceTop = h - footH - price * 1.45 - k, labelTop = priceTop - label * 1.55;
        double stageTop = headH + k, stageH = Math.Max(6 * k, labelTop - stageTop - k);
        double ks = Math.Min(k, stageH / 52), cy = stageTop + (stageH - 50 * ks) / 2 + 20 * ks;
        _cx = w / 2;
        Box(0, 0, w, h, "#12040F");
        Box(0, 0, w, headH, "#2A0A20");
        Line("SHOP AT HOME", title, Gold, Math.Max(0, (headH - title * 1.35) / 2));
        Box(_cx - 26 * ks, cy + 24 * ks, 52 * ks, Math.Max(1.5, 6 * ks), "#2A0A20");
        _bodyWidth = (item == 0 ? 34 : item == 1 ? 30 : 36) * ks;
        double bodyHeight = (item == 0 ? 40 : item == 1 ? 32 : 36) * ks;
        _body = Box(_cx - _bodyWidth / 2, cy - bodyHeight / 2, _bodyWidth, bodyHeight, colors[item]);
        Line(names[item], label, Brush.Parse("#C9A0DC"), labelTop);
        _price = Line("", price, Gold, priceTop);
        _foot = Box(0, h - footH, w, footH, "#E8836F");
        _call = Line("CALL NOW", call, Brush.Parse("#1A1A2E"), h - footH + Math.Max(0, (footH - call * 1.35) / 2));
        Tick(0);
    }

    internal void Tick(double ms)
    {
        double width = Math.Max(1, _bodyWidth * Squeeze[(int)(ms / 220) & 3]);
        _body.Width = width;
        Canvas.SetLeft(_body, _cx - width / 2);
        int price = Math.Max(9, _start - (int)(ms / 90) * _drop);
        _price.Text = price.ToString(CultureInfo.InvariantCulture) + " SP";
        bool lit = ((int)(ms / (price == 9 ? 250 : 500)) & 1) == 0;
        _foot.Fill = lit ? Coral : Brush.Parse("#3A1420");
        _call.Foreground = Brush.Parse(lit ? "#1A1A2E" : "#7A4050");
        _price.Opacity = price == 9 && !lit ? .45 : 1;
    }

    private static double Pixel(double wanted, double fits, double floor) => Math.Max(floor, Math.Round(Math.Min(wanted, fits)));
    private Rectangle Box(double x, double y, double w, double h, string color)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = Brush.Parse(color), IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); _host.Children.Add(r); return r;
    }
    private TextBlock Line(string text, double size, IBrush ink, double top)
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"), FontSize = size, Foreground = ink, Width = _host.Width, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
        Canvas.SetTop(t, top); _host.Children.Add(t); return t;
    }
}
