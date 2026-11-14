using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF ConditioningControlPanel/Services/EmiDesk/EmiChannels.Browsing.cs:35.
internal sealed class BrowsingPainter
{
    private const double RefGlass = 152.0;
    private const double PageMs = 2500.0;
    private const double BlinkMs = 500.0;
    private const double DropAtMs = 1400.0;
    private const double DeepRollMs = 1000.0;

    private const int Mail = 0, Board = 1, Bank = 2, Cards = 3, Deep = 4, Rec = 5;
    private const int PageCount = 6;
    private static readonly SolidColorBrush DarkBrush =
        new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly SolidColorBrush RowBrush =
        new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44));
    private static readonly SolidColorBrush BarBrush =
        new SolidColorBrush(Color.FromRgb(0x43, 0x43, 0x6A));
    private static readonly SolidColorBrush BlockBrush =
        new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x3F));
    private static readonly SolidColorBrush LabelBrush =
        new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x7A));
    private static readonly SolidColorBrush DropBrush =
        new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x6F));
    private static readonly SolidColorBrush[] Accents =
    {
        new SolidColorBrush(Color.FromRgb(0x7F, 0xD4, 0xC1)),
        new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)),
        new SolidColorBrush(Color.FromRgb(0x8C, 0xC6, 0x3F)),
        new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xDC)),
        new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC)),
        new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x6F))
    };
    private static readonly string[] Titles = { "MAIL", "BOARD", "BANK", "CARDS", "DEEP", "REC" };

    private readonly double _w, _h, _k;
    private readonly double _font, _small, _big, _barH, _body, _pad;
    private readonly Random _rng = new();
    private readonly Canvas[] _pages = new Canvas[PageCount];
    private readonly int[] _order = new int[PageCount];
    private readonly List<Rectangle> _mailBars = new();
    private readonly List<Rectangle> _boardBars = new();
    private readonly Rectangle _litRow = new();
    private readonly Rectangle _litDot = new();
    private readonly Rectangle _litBar = new();
    private readonly TextBlock _bankNum = new();
    private readonly TextBlock _bankDrop = new();
    private readonly Polyline _deepTrace = new();
    private readonly global::Avalonia.Collections.AvaloniaList<Point> _deepPts = new();

    private double _mailBarMax, _boardBarMax, _deepStep, _deepTop;
    private int _live = -1, _litState = -1, _bankN = -1;
    private double _pageUpMs, _deepRolledMs;

    public BrowsingPainter(double w, double h)
    {
        _w = w; _h = h;
        _k = Math.Max(0.5, w / RefGlass);

        _font = Math.Max(5.0, 8.0 * _k);
        _small = Math.Max(5.0, 7.0 * _k);
        _big = Math.Max(9.0, 16.0 * _k);
        _barH = Math.Max(_font + 3.0, 13.0 * _k);
        _pad = Math.Max(2.0, 6.0 * _k);
        _body = Math.Max(8.0, _h - _barH);

        for (int i = 0; i < PageCount; i++) _order[i] = i;
        for (int i = PageCount - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
    }

    public string Id => "browsing";

    public void Attach(Panel host)
    {

        host.Children.Add(Box(0, 0, _w, _h, Brush.Parse("#0E0E1C"), 1));

        for (int i = 0; i < PageCount; i++)
        {
            var page = new Canvas
            {
                Width = _w,
                Height = _h,
                ClipToBounds = true,
                IsHitTestVisible = false,
                IsVisible = false
            };
            page.Children.Add(Box(0, 0, _w, _barH, Accents[i], 1.0));
            page.Children.Add(LeftText(new TextBlock(), Titles[i], Math.Max(2, 4 * _k),
                Math.Max(0, (_barH - _font * 1.3) / 2), _font, DarkBrush));

            switch (i)
            {
                case Mail: BuildMail(page); break;
                case Board: BuildBoard(page); break;
                case Bank: BuildBank(page, Accents[i]); break;
                case Cards: BuildCards(page, Accents[i]); break;
                case Deep: BuildDeep(page, Accents[i]); break;
                default: BuildRec(page, Accents[i]); break;
            }

            _pages[i] = page;
            host.Children.Add(page);
        }
        for (double y = 0; y < _h; y += 2) host.Children.Add(Box(0, y, _w, 1, Brush.Parse("#33000000"), 1));
    }

    public void Tick(double tMs)
    {
        int slot = tMs <= 0 ? 0 : (int)(tMs / PageMs);
        int page = _order[slot % PageCount];

        if (page != _live)
        {
            if (_live >= 0) _pages[_live].IsVisible = false;
            _live = page;
            _pageUpMs = tMs;
            _bankN = -1;
            _litState = -1;
            _pages[page].IsVisible = true;
            Reroll(page, tMs);
        }

        double dwell = tMs - _pageUpMs;

        switch (page)
        {
            case Board:
                Blink(tMs);
                break;

            case Bank:

                int n = 1200 + (int)(tMs / 40.0);
                if (n != _bankN)
                {
                    _bankN = n;
                    _bankNum.Text = n.ToString(CultureInfo.InvariantCulture);
                }

                var want = dwell > DropAtMs;
                if (_bankDrop.IsVisible != want) _bankDrop.IsVisible = want;
                break;

            case Deep:

                if (tMs - _deepRolledMs >= DeepRollMs) RollDeep(tMs);
                break;
        }
    }

    private void BuildMail(Canvas page)
    {
        double top = _barH + _pad;
        double pitch = Math.Max(3.0, (_h - top - _pad * 0.5) / 5.0);
        double rowH = Math.Max(_small + 1.0, pitch * 0.78);
        double x = _pad, w = Math.Max(4.0, _w - _pad * 2);
        _mailBarMax = Math.Max(3.0, w - _pad * 1.2);

        for (int r = 0; r < 5; r++)
        {
            double y = top + r * pitch;
            if (r == 1)
            {
                page.Children.Add(Box(x, y, w, rowH, PinkBrush, 1.0));
                page.Children.Add(LeftText(new TextBlock(), "re: emi", x + _pad * 0.6,
                    y + Math.Max(0, (rowH - _small * 1.3) / 2), _small, DarkBrush));
            }
            else
            {
                page.Children.Add(Box(x, y, w, rowH, RowBrush, 1.0));
                var bar = Box(x + _pad * 0.6, y + rowH * 0.42, _mailBarMax * 0.5,
                    Math.Max(1.5, 4 * _k), BarBrush, 1.0);
                _mailBars.Add(bar);
                page.Children.Add(bar);
            }
        }
    }

    private void BuildBoard(Canvas page)
    {
        double top = _barH + _pad;
        double pitch = Math.Max(4.0, (_h - top - _pad * 0.5) / 4.0);
        double rowH = Math.Max(3.0, pitch * 0.72);
        double x = _pad, w = Math.Max(4.0, _w - _pad * 2);
        double dot = Math.Max(2.0, 4 * _k);
        double barH = Math.Max(1.5, 4 * _k);
        _boardBarMax = Math.Max(3.0, w - _pad * 1.6 - dot * 2);

        for (int r = 0; r < 4; r++)
        {
            double y = top + r * pitch;
            bool tracked = r == 2;

            var row = tracked ? _litRow : new Rectangle();
            Place(row, w, rowH, BlockBrush, x, y);
            page.Children.Add(row);

            var pip = tracked ? _litDot : new Rectangle();
            Place(pip, dot, dot, Accents[Board], x + _pad * 0.6, y + rowH * 0.16);
            page.Children.Add(pip);

            var bar = tracked ? _litBar : new Rectangle();
            Place(bar, _boardBarMax * 0.6, barH, BarBrush, x + _pad * 0.6 + dot * 2, y + rowH * 0.42);
            if (!tracked) _boardBars.Add(bar);
            page.Children.Add(bar);
        }
    }

    private void BuildBank(Canvas page, Brush accent)
    {
        page.Children.Add(Centered(new TextBlock(), "BALANCE", _barH + _body * 0.09, _small, LabelBrush));
        page.Children.Add(Centered(_bankNum, "1200", _barH + _body * 0.22, _big, accent));

        _bankDrop.IsVisible = false;
        page.Children.Add(Centered(_bankDrop, "-14", _barH + _body * 0.50, Math.Max(6, 10 * _k), DropBrush));

        page.Children.Add(Box(_pad * 1.5, _barH + _body * 0.72,
            Math.Max(4.0, _w - _pad * 3), Math.Max(3.0, _body * 0.16), BlockBrush, 1.0));
    }

    private void BuildCards(Canvas page, Brush accent)
    {
        double cardX = _w * 0.13, cardW = Math.Max(6.0, _w * 0.74);
        double cardY = _barH + _body * 0.11, cardH = Math.Max(6.0, _body * 0.49);
        page.Children.Add(Box(cardX, cardY, cardW, cardH, CreamBrush, 1.0));

        page.Children.Add(Centered(new TextBlock(), "NOW", cardY + cardH * 0.08, _font, DarkBrush));
        page.Children.Add(Centered(new TextBlock(), "SERVING", cardY + cardH * 0.36, _font, DarkBrush));
        page.Children.Add(Centered(new TextBlock(), "004", cardY + cardH * 0.64,
            Math.Max(7, 12 * _k), PinkBrush));

        page.Children.Add(Box(cardX, _barH + _body * 0.72, cardW, Math.Max(2.0, 8 * _k), accent, 1.0));
    }

    private void BuildDeep(Canvas page, Brush accent)
    {
        _deepStep = Math.Max(3.0, 9 * _k);
        _deepTop = _barH + _body * 0.07;

        int count = Math.Max(4, (int)((_w - _pad * 2) / _deepStep) + 1);
        for (int i = 0; i < count; i++) _deepPts.Add(new Point(_pad + i * _deepStep, _deepTop));

        _deepTrace.Points = _deepPts;
        _deepTrace.Stroke = accent;
        _deepTrace.StrokeThickness = Math.Max(1.0, 2 * _k);
        _deepTrace.StrokeJoin = PenLineJoin.Round;
        _deepTrace.IsHitTestVisible = false;
        page.Children.Add(_deepTrace);

        page.Children.Add(LeftText(new TextBlock(), "DEPTH", _pad,
            _barH + Math.Max(1.0, 2 * _k), _small, LabelBrush));
    }

    private void BuildRec(Canvas page, Brush accent)
    {
        double x0 = _w * 0.053, pitchX = _w * 0.224, tileW = Math.Max(3.0, _w * 0.184);
        double y0 = _barH + _body * 0.07, pitchY = _body * 0.29, tileH = Math.Max(3.0, _body * 0.24);

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                bool lit = r == 1 && c == 2;
                page.Children.Add(Box(x0 + c * pitchX, y0 + r * pitchY, tileW, tileH,
                    lit ? accent : BlockBrush, 1.0));
            }
        }
    }

    private void Reroll(int page, double tMs)
    {
        switch (page)
        {
            case Mail:
                foreach (var bar in _mailBars) bar.Width = _mailBarMax * (0.30 + _rng.NextDouble() * 0.36);
                break;
            case Board:
                foreach (var bar in _boardBars) bar.Width = _boardBarMax * (0.42 + _rng.NextDouble() * 0.29);
                _litBar.Width = _boardBarMax * (0.42 + _rng.NextDouble() * 0.29);
                Blink(tMs);
                break;
            case Deep:
                RollDeep(tMs);
                break;
        }
    }
    private void Blink(double tMs)
    {
        int lit = (int)(tMs / BlinkMs) % 2 == 0 ? 1 : 0;
        if (lit == _litState) return;
        _litState = lit;

        _litRow.Fill = lit == 1 ? Accents[Board] : BlockBrush;
        _litDot.Fill = lit == 1 ? DarkBrush : Accents[Board];
        _litBar.Fill = lit == 1 ? DarkBrush : BarBrush;
    }

    private void RollDeep(double tMs)
    {
        _deepRolledMs = tMs;
        double y = _deepTop;
        for (int i = 0; i < _deepPts.Count; i++)
        {
            _deepPts[i] = new Point(_pad + i * _deepStep, Math.Min(y, _h + 20 * _k));
            y += (4 + _rng.NextDouble() * 10) * _k;
        }
    }

    private static void Place(Rectangle r, double w, double h, Brush fill, double x, double y)
    {
        r.Width = Math.Max(0.5, w);
        r.Height = Math.Max(0.5, h);
        r.Fill = fill;
        r.IsHitTestVisible = false;
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
    }
    private TextBlock Centered(TextBlock tb, string s, double top, double size, Brush ink)
    {
        tb.Text = s;
        tb.FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
        tb.FontSize = size;
        tb.Foreground = ink;
        tb.Width = _w;
        tb.TextAlignment = TextAlignment.Center;
        tb.IsHitTestVisible = false;
        Canvas.SetLeft(tb, 0);
        Canvas.SetTop(tb, top);
        return tb;
    }

    private TextBlock LeftText(TextBlock tb, string s, double x, double top, double size, Brush ink)
    {
        tb.Text = s;
        tb.FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
        tb.FontSize = size;
        tb.Foreground = ink;
        tb.IsHitTestVisible = false;
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, top);
        return tb;
    }

    private static readonly Brush PinkBrush = Brush.Parse("#FF69B4"), CreamBrush = Brush.Parse("#F5F0E1");
    private static Rectangle Box(double x, double y, double w, double h, Brush fill, double opacity)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill, Opacity = opacity, IsHitTestVisible = false };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); return r;
    }
}
