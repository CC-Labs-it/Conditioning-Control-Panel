using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiChannels.Shop.cs:34-355. A broadcast, not a storefront.
internal sealed class ShopPainter
{
    private readonly Canvas _host;
    private readonly Rectangle _body, _foot;
    private readonly Rectangle[] _detail;
    private readonly int _item;
    private readonly double _ks, _cy;
    private readonly TextBlock _price, _call;
    private readonly double _cx, _bodyWidth;
    private readonly int _start, _drop;
    private static readonly double[] Squeeze = { 1, .6, .15, .6 };
    private static readonly IBrush Gold = Brush.Parse("#F2C14E"), Coral = Brush.Parse("#E8836F");

    internal ShopPainter(Canvas host, int item)
    {
        _host = host;
        double w = host.Width, h = host.Height, k = Math.Max(.35, w / 152);
        string[] names = { Loc.Get("emi_shop_card"), Loc.Get("emi_shop_watch"), Loc.Get("emi_shop_disc") };
        int[] starts = { 480, 1290, 760 };
        string[] colors = { "#F5F0E1", "#F2C14E", "#FF69B4" };
        item = Math.Clamp(item, 0, 2);
        _item = item;
        _start = starts[item];
        _drop = Math.Max(7, (int)Math.Ceiling((_start - 9) / 81.0));
        double title = Pixel(7 * k, w / 13.5, 4), call = Pixel(7 * k, w / 8.5, 4);
        double label = Pixel(7 * k, w / 6, 4), price = Pixel(12 * k, w / 7.5, 6);
        double headH = Math.Max(title * 1.6, 16 * k), footH = Math.Max(call * 1.6, 14 * k);
        double priceTop = h - footH - price * 1.45 - k, labelTop = priceTop - label * 1.55;
        double stageTop = headH + k, stageH = Math.Max(6 * k, labelTop - stageTop - k);
        double ks = Math.Min(k, stageH / 52), cy = stageTop + (stageH - 50 * ks) / 2 + 20 * ks;
        _cx = w / 2; _cy = cy; _ks = ks;
        Box(0, 0, w, h, "#12040F");
        Box(0, 0, w, headH, "#2A0A20");
        Line(Loc.Get("emi_shop_title"), title, Gold, Math.Max(0, (headH - title * 1.35) / 2));
        Box(_cx - 26 * ks, cy + 24 * ks, 52 * ks, Math.Max(1.5, 6 * ks), "#2A0A20");
        _bodyWidth = (item == 0 ? 34 : item == 1 ? 30 : 36) * ks;
        double bodyHeight = (item == 0 ? 40 : item == 1 ? 32 : 36) * ks;
        _body = Box(_cx - _bodyWidth / 2, cy - bodyHeight / 2, _bodyWidth, bodyHeight, colors[item]);
        _detail = new Rectangle[item == 0 ? 4 : 1];
        for (int i = 0; i < _detail.Length; i++)
            _detail[i] = Box(0, 0, 1, 1, item == 2 ? "#12040F" : "#2A0A20");
        Line(names[item], label, Brush.Parse("#C9A0DC"), labelTop);
        _price = Line("", price, Gold, priceTop);
        _foot = Box(0, h - footH, w, footH, "#E8836F");
        _call = Line(Loc.Get("emi_shop_call"), call, Brush.Parse("#1A1A2E"), h - footH + Math.Max(0, (footH - call * 1.35) / 2));
        // WPF Shop.cs:218-226: black scanlines above the whole broadcast.
        double pitch = Math.Max(2, 2 * k);
        for (double y = 0; y < h; y += pitch)
        {
            var line = Box(0, y, w, Math.Min(pitch / 2, h - y), "#000000");
            line.Opacity = .18;
        }
        Tick(0);
    }

    internal void Tick(double ms)
    {
        double width = Math.Max(2 * _ks, _bodyWidth * Squeeze[(int)(ms / 220) & 3]);
        _body.Width = width;
        Canvas.SetLeft(_body, _cx - width / 2);
        double left = _cx - width / 2;
        if (_item == 0)
        {
            for (int i = 0; i < _detail.Length; i++)
            {
                double px = (4 + i * 7) * _ks;
                Show(_detail[i], px + 3 * _ks <= width, left + px, _cy - 12 * _ks, 3 * _ks, 3 * _ks);
            }
        }
        else if (_item == 1)
            Show(_detail[0], width > 10 * _ks, left + 4 * _ks, _cy - 12 * _ks, width - 8 * _ks, 24 * _ks);
        else
            Show(_detail[0], width > 12 * _ks, _cx - width / 4, _cy - 9 * _ks, width / 2, 18 * _ks);
        int price = Math.Max(9, _start - (int)(ms / 90) * _drop);
        _price.Text = price.ToString(CultureInfo.InvariantCulture) + " SP";
        bool lit = ((int)(ms / (price == 9 ? 250 : 500)) & 1) == 0;
        _foot.Fill = lit ? Coral : Brush.Parse("#3A1420");
        _call.Foreground = Brush.Parse(lit ? "#1A1A2E" : "#7A4050");
        _price.Opacity = price == 9 && !lit ? .45 : 1;
    }

    private static void Show(Rectangle r, bool visible, double x, double y, double w, double h)
    {
        r.IsVisible = visible;
        if (!visible) return;
        r.Width = Math.Max(.5, w); r.Height = Math.Max(.5, h);
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
    }

    private static double Pixel(double wanted, double fits, double floor) => Math.Max(floor, Math.Round(Math.Min(wanted, fits)));
    private Rectangle Box(double x, double y, double w, double h, string color)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = Brush.Parse(color), IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); _host.Children.Add(r); return r;
    }
    private TextBlock Line(string text, double size, IBrush ink, double top)
    {
        size = Math.Min(size, _host.Width / Math.Max(1, text.Length * 1.1));
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"), FontSize = size, Foreground = ink, Width = _host.Width, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
        Canvas.SetTop(t, top); _host.Children.Add(t); return t;
    }
}
