// WPF RefreshTag and PlaceCalendarTag. Adapted from parity/trunk d5052645908a.
// ponytail: the tag's swing/tug and tonight's bob/comet remain with the other tab Fx.
using System;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private void PaperInit()
    {
        SpiralPath.Data = GroundSpiral();
        Ambient.StartLayers(new AmbientFxConfig
        {
            Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField, Intensity = 0.85, FogPuffs = 3,
        });
        HeroArt.Source = Helpers.ModArt.TryLoad("features/Phrase_Lock.png", 760);
        HeroArtPlate.SizeChanged += (_, _) => PaintHeroArtFade();
        ActualThemeVariantChanged += (_, _) => PaintHeroArtFade();
        PaperTag.PointerReleased += (_, _) => ToggleBill();
        Calendar.SizeChanged += (_, _) => PlaceCalendarTag(); // WPF :105
        CalendarTag.SizeChanged += (_, _) => PlaceCalendarTag();
        PaintHeroArtFade();
    }

    // WPF BuildSpiral. ponytail: the watermark remains static until the ground motion pass.
    private static Geometry GroundSpiral()
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        const double c = 380, inner = 8, turns = 6.5, step = Math.PI / 45;
        var maxTheta = turns * Math.PI * 2;
        var growth = (c - inner - 4) / maxTheta;
        ctx.BeginFigure(new Point(c + inner, c), false);
        for (var theta = step; theta <= maxTheta; theta += step)
        {
            var radius = inner + growth * theta;
            ctx.LineTo(new Point(c + radius * Math.Cos(theta), c + radius * Math.Sin(theta)));
        }
        ctx.EndFigure(false);
        return geometry;
    }

    private void PaintHeroArtFade()
    {
        if (Brush("SurfaceBgBrush") is not ISolidColorBrush surface) return;
        var c = surface.Color;
        HeroArtFade.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(c, 0), new(Color.FromArgb(0xD0, c.R, c.G, c.B), 0.28),
                new(Color.FromArgb(0x40, c.R, c.G, c.B), 0.62), new(Colors.Transparent, 1),
            },
        };
    }

    internal void RefreshTag(int balance)
    {
        TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
        var ink = Color.Parse(balance > 0 ? "#C8244A" : balance < 0 ? "#1E8A6E" : "#241A2E");
        TxtTagAmount.Foreground = new SolidColorBrush(ink);
        var c = ChasterHead.Service;
        var line = TabPageText.Tag(balance, c?.PushableTodaySeconds ?? 0, c?.IsPaused == true,
            !string.IsNullOrEmpty(CoreSettings.Current.ChasterLockId) && c?.LockLookup != LockLookup.Ambiguous, c?.AddsBlocked == true);
        TxtTagLands.Text = line.Today == null ? Loc.Get(line.Key) : Loc.GetF(line.Key, line.Today, line.Later!);
        TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
        TagStamp.BorderBrush = TxtTagStamp.Foreground = new SolidColorBrush(balance == 0 ? Color.Parse("#6E6686") : ink);
        TxtCalendarTag.Text = CircesTab.Format(balance);
        CalendarTag.IsVisible = balance > 0 && _tonightCell != null;
        PlaceCalendarTag();
    }

    private void PlaceCalendarTag()
    {
        if (_tonightCell is not { Bounds.Width: > 0 } cell || cell.TranslatePoint(default, CalendarRow) is not { } p) return;
        CalendarTag.Margin = new Thickness(Math.Max(0, p.X + cell.Bounds.Width / 2 - CalendarTag.Bounds.Width / 2),
            p.Y + cell.Bounds.Height - 14, 0, 0);
    }
}
