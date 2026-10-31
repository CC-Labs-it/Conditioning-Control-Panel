using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal sealed class NavCoinParts
    {
        internal Ellipse Disc = null!;
        internal Border Dish = null!, Socket = null!, Rim = null!, Outer = null!, Inner = null!;
        internal Path Specular = null!;
        internal TranslateTransform Lift = new(), DiscShift = new(1.5, 0);
        internal RotateTransform Tilt = new();
        internal BeatLoop? Beat, TiltBeat;
        internal double TiltFrom, TiltTo, TiltDuration;
        internal bool Settling;
        internal double From, To, ShadowFrom, ShadowTo, OpacityFrom, OpacityTo, Duration;
        internal bool Spring;
    }

    internal static void BuildNavCoin(NavSectionRow row, Panel face)
    {
        var tile = row.Tile!;
        var coin = row.Coin = new NavCoinParts();
        row.Face = face;
        IBrush? Brush(string key) => face.TryFindResource(key, out var value) ? value as IBrush : null;
        Border Layer(double size, double radius, string key, bool fill, double thickness = 1) => new()
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Background = fill ? Brush(key) : null,
            BorderBrush = fill ? null : Brush(key), BorderThickness = new Thickness(fill ? 0 : thickness),
        };
        coin.Disc = new Ellipse
        {
            Width = 54, Height = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -6),
            IsHitTestVisible = false, RenderTransform = coin.DiscShift,
            Fill = new RadialGradientBrush
            {
                GradientStops = { new(Color.FromUInt32(DepthLaw.ShadowColor(row.Hue.ToUInt32())), 0),
                    new(Color.FromUInt32(DepthLaw.ShadowColor(row.Hue.ToUInt32(), 0x5C / 255.0)), 0.6),
                    new(Color.FromUInt32(DepthLaw.ShadowColor(row.Hue.ToUInt32(), 0)), 1) },
            },
        };
        coin.Dish = Layer(tile.Width, tile.CornerRadius.TopLeft, "DepthCoinDish", true);
        coin.Socket = Layer(50, 12, "DepthCoinSocket", true);
        coin.Rim = Layer(50, 12, "DepthCoinRim", false);
        coin.Outer = Layer(58, 16, "DepthCoinOuterRim", false);
        coin.Inner = Layer(48, 11, "DepthCoinInnerShadow", false, 2.5);
        var sliver = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 50, 50), 12, 12),
            new RectangleGeometry(new Rect(2.5, 3, 50, 50), 12, 12));
        coin.Specular = new Path
        {
            Width = 50, Height = 50, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Data = new CombinedGeometry(GeometryCombineMode.Intersect, sliver,
                new EllipseGeometry(new Rect(9.6 - 24, 9.6 - 24, 48, 48))), Fill = Brush("DepthCoinSpecular"),
        };
        foreach (var part in new Control[] { coin.Disc, coin.Dish, coin.Socket, coin.Rim, coin.Outer, coin.Inner, coin.Specular })
            Grid.SetRow(part, Grid.GetRow(tile));
        var kids = face.Children;
        kids.Insert(kids.IndexOf(tile), coin.Disc);
        kids.Insert(kids.IndexOf(tile), coin.Outer);
        kids.Insert(kids.IndexOf(tile) + 1, coin.Dish);
        int over = row.Tint != null ? kids.IndexOf(row.Tint) : kids.IndexOf(tile);
        kids.Insert(over + 1, coin.Socket);
        kids.Insert(over + 2, coin.Rim);
        kids.Insert(over + 3, coin.Inner);
        kids.Insert(row.Ring != null ? kids.IndexOf(row.Ring) + 1 : over + 4, coin.Specular);
        face.RenderTransformOrigin = new RelativePoint(0.5, 0.4, RelativeUnit.Relative);
        face.RenderTransform = new TransformGroup { Children = { coin.Tilt, coin.Lift } };
        coin.Beat = new BeatLoop(face, seconds => StepNavCoin(coin, seconds));
        coin.TiltBeat = new BeatLoop(face, seconds =>
        {
            if (AmbientFxCanvas.Env.Level == Models.MotionLevel.Off || !coin.Settling &&
                (row.Pressed || !NavCoinLaw.Tilts(row.Active, AmbientFxCanvas.Env.Level, AmbientFxCanvas.Env.CurrentTier)))
            { AnimateNavTilt(row, 0, settling: true); return; }
            double p = Math.Clamp(seconds / coin.TiltDuration, 0, 1);
            coin.Tilt.Angle = coin.Settling ? NavCoinLaw.Travel(coin.TiltFrom, coin.TiltTo, p, false)
                : coin.TiltFrom + (coin.TiltTo - coin.TiltFrom) * p;
            if (p >= 1) coin.TiltBeat!.Stop();
        });
        face.DetachedFromVisualTree += (_, _) => { coin.Beat.Stop(); coin.TiltBeat.Stop(); StepNavCoin(coin, coin.Duration); };
        row.Button.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(row.Button).Properties.IsLeftButtonPressed) return;
            row.Pressed = true; PaintNavCoin(row);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        row.Button.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            if (!row.Pressed) return;
            row.Pressed = false; row.Hovered = row.Button.IsPointerOver;
            PaintNavCoin(row, spring: true);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        row.Button.PointerExited += (_, _) => AnimateNavTilt(row, 0, settling: true);
        row.Button.PointerMoved += (_, e) =>
        {
            if (row.Pressed || !NavCoinLaw.Tilts(row.Active, AmbientFxCanvas.Env.Level, AmbientFxCanvas.Env.CurrentTier))
            { AnimateNavTilt(row, 0, settling: true); return; }
            if (tile.Bounds.Width <= 0 || tile.Bounds.Height <= 0) return;
            var at = e.GetPosition(tile);
            AnimateNavTilt(row, NavCoinLaw.Tilt(at.X / tile.Bounds.Width * 2 - 1, at.Y / tile.Bounds.Height * 2 - 1));
        };
    }

    internal static void AnimateNavTilt(NavSectionRow row, double target, bool settling = false)
    {
        var c = row.Coin!;
        c.TiltBeat!.Stop(); // replace the old track before either settling or snapping
        c.TiltFrom = c.Tilt.Angle;
        c.TiltTo = target;
        c.Settling = settling;
        c.TiltDuration = DepthLaw.Ms(settling ? DepthLaw.HoverMs : 90, AmbientFxCanvas.Env.Level) / 1000.0;
        if (c.TiltDuration <= 0 || c.TiltFrom == target) { c.Tilt.Angle = target; return; }
        c.TiltBeat.Start();
    }

    internal static void PaintNavCoin(NavSectionRow row, bool spring = false)
    {
        if (row.Coin is not { } c) return;
        bool seated = row.Pressed || row.Active;
        if (row.Face!.TryFindResource(seated ? "DepthPressedBevel" : "DepthCoinRim", out var brush)) c.Rim.BorderBrush = brush as IBrush;
        c.Socket.Opacity = seated ? 1 : 0;
        AnimateNavTilt(row, 0, settling: true);
        c.Beat!.Stop();
        c.From = c.Lift.Y;
        c.To = DepthLaw.TravelFor(true, row.Pressed, row.Active, row.Hovered);
        c.ShadowFrom = c.DiscShift.Y;
        double shadow = DepthLaw.ShadowFor(true, row.Pressed, row.Active, row.Hovered);
        c.ShadowTo = shadow > 0 ? shadow : c.ShadowFrom;
        c.OpacityFrom = c.Disc.Opacity;
        c.OpacityTo = shadow > 0 ? 1 : 0;
        c.Spring = spring;
        c.Duration = DepthLaw.Ms(row.Pressed ? DepthLaw.PressMs : spring ? DepthLaw.ReleaseMs : DepthLaw.HoverMs,
            AmbientFxCanvas.Env.Level) / 1000.0;
        if (c.Duration <= 0 || TopLevel.GetTopLevel(row.Face) == null) StepNavCoin(c, c.Duration);
        else c.Beat.Start();
    }

    internal static void StepNavCoin(NavCoinParts c, double seconds)
    {
        double p = c.Duration <= 0 ? 1 : Math.Clamp(seconds / c.Duration, 0, 1);
        c.Lift.Y = NavCoinLaw.Travel(c.From, c.To, p, c.Spring);
        c.DiscShift.Y = NavCoinLaw.Travel(c.ShadowFrom, c.ShadowTo, p, false);
        c.Disc.Opacity = c.OpacityFrom + (c.OpacityTo - c.OpacityFrom) * p;
        if (p >= 1) c.Beat?.Stop();
    }

    internal void PaintDepthRail(Color hue)
    {
        if (Named<Rectangle>("NavRailShadow") is not { } shadow) return;
        shadow.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), 0xBF / 255.0)), 0),
                new(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), 0)), 1) },
        };
    }
}
