// PORTED from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs:67-497; Core DepthLaw, Theme/Depth.xaml.

using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF NavCoinParts (:78): the coin's parts around the authored tile.</summary>
        internal sealed class NavCoinParts
        {
            internal Ellipse Disc = null!;
            internal readonly TranslateTransform DiscShift = new();
            internal Border Dish = null!, Socket = null!, Rim = null!, Outer = null!, Inner = null!;
            internal Path Specular = null!;
        }

        // WPF Controls/NavRail/NavRailRules.cs:213-220: the coin's sizes and the tilt's 90 ms.
        internal const double CoinDiscWidth = 54, CoinDiscHeight = 12, CoinRimSize = 50, CoinRimRadius = 12;
        internal const double CoinOuterSize = 58, CoinOuterRadius = 16;
        internal const double CoinInnerSize = 48, CoinInnerRadius = 11, CoinInnerThickness = 2.5;
        internal const double SpecularOffsetX = 2.5, SpecularOffsetY = 3.0, CoinDiscRightPx = 1.5;
        internal const int CoinTiltMs = 90;

        /// <summary>WPF NavRailRules.CoinOvershoot: past the target by ReleaseOvershootPx, then back.</summary>
        internal static double CoinOvershoot(double from, double to) =>
            to == from ? to : to + Math.Sign(to - from) * DepthLaw.ReleaseOvershootPx;

        /// <summary>WPF NavRailRules.CoinTilt: the lean toward a pointer at nx, ny in -1..1.</summary>
        internal static double CoinTilt(double nx, double ny) =>
            Math.Clamp(nx, -1, 1) * -Math.Clamp(ny, -1, 1) * DepthLaw.TiltDegrees;

        /// <summary>WPF NavRailRules.CoinTilts + DepthRules.TiltAllowed: idle coins lean, never at
        /// Motion Off or on the Performance tier.</summary>
        internal static bool CoinTilts(bool active, MotionLevel level, PerformanceTier tier) =>
            !active && level != MotionLevel.Off && tier != PerformanceTier.Performance;

        private static IBrush? DepthBrush(string key)
        {
            try { return Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var v) == true ? v as IBrush : null; }
            catch { return null; }
        }

        private static Color Shadow(Color hue, double alpha) => Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), alpha));

        /// <summary>WPF BuildNavCoin (:245): the disc under the tile, the outer hairline over the disc,
        /// the dish right over the tile, socket/lip/inner ring over the art and its hue wash, and the
        /// crescent over the ring.</summary>
        internal static NavCoinParts BuildNavCoin(Panel face, Border tile, Border? tint, Border? ring, Border? art, Color hue)
        {
            var coin = new NavCoinParts();
            int row = Grid.GetRow(tile);
            Border Part(double size, double radius, double thickness) => new()
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(thickness),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            coin.Disc = new Ellipse
            {
                Width = CoinDiscWidth, Height = CoinDiscHeight,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -CoinDiscHeight / 2),
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Shadow(hue, DepthLaw.ShadowAlpha), 0),
                        new GradientStop(Shadow(hue, 0x5C / 255.0), 0.6),
                        new GradientStop(Shadow(hue, 0), 1),
                    },
                },
                RenderTransform = coin.DiscShift, IsHitTestVisible = false,
            };
            coin.Dish = Part(tile.Width, 0, 0);
            coin.Dish.Height = tile.Height;
            coin.Dish.CornerRadius = tile.CornerRadius;
            coin.Dish.Background = DepthBrush("DepthCoinDish");
            coin.Socket = Part(CoinRimSize, CoinRimRadius, 0);
            coin.Socket.Opacity = 0;
            coin.Socket.Background = DepthBrush("DepthCoinSocket");
            coin.Rim = Part(CoinRimSize, CoinRimRadius, 1);
            coin.Rim.BorderBrush = DepthBrush("DepthCoinRim");
            coin.Outer = Part(CoinOuterSize, CoinOuterRadius, 1);
            coin.Outer.BorderBrush = DepthBrush("DepthCoinOuterRim");
            coin.Inner = Part(CoinInnerSize, CoinInnerRadius, CoinInnerThickness);
            coin.Inner.BorderBrush = DepthBrush("DepthCoinInnerShadow");
            coin.Specular = new Path
            {
                Data = BuildNavCoinCrescent(), Width = CoinRimSize, Height = CoinRimSize, Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = DepthBrush("DepthCoinSpecular"), IsHitTestVisible = false,
            };
            coin.DiscShift.X = CoinDiscRightPx;
            foreach (var part in new Control[] { coin.Disc, coin.Dish, coin.Socket, coin.Rim, coin.Outer, coin.Inner, coin.Specular })
                Grid.SetRow(part, row);

            var kids = face.Children;
            kids.Insert(kids.IndexOf(tile), coin.Disc);
            kids.Insert(kids.IndexOf(tile) + 1, coin.Dish);
            // Over the art and its hue wash (the art itself when a row has no wash), under the ring.
            Control? over = tint ?? (Control?)art;
            int at = over != null && kids.Contains(over) ? kids.IndexOf(over) + 1 : kids.IndexOf(coin.Dish) + 1;
            kids.Insert(at, coin.Socket);
            kids.Insert(at + 1, coin.Rim);
            kids.Insert(at + 2, coin.Inner);
            kids.Insert(kids.IndexOf(coin.Disc) + 1, coin.Outer);
            kids.Insert(ring != null && kids.Contains(ring) ? kids.IndexOf(ring) + 1 : kids.IndexOf(coin.Inner) + 1, coin.Specular);
            return coin;
        }

        /// <summary>WPF BuildNavCoinCrescent (:359): the sliver between the face and the face nudged
        /// down-right, kept only near the top-left corner.</summary>
        internal static Geometry BuildNavCoinCrescent()
        {
            double s = CoinRimSize, r = CoinRimRadius;
            var face = new RectangleGeometry(new Rect(0, 0, s, s), r, r);
            var nudged = new RectangleGeometry(new Rect(SpecularOffsetX, SpecularOffsetY, s, s), r, r);
            var sliver = new CombinedGeometry(GeometryCombineMode.Exclude, face, nudged);
            var corner = new EllipseGeometry { Center = new Point(r * 0.8, r * 0.8), RadiusX = s * 0.48, RadiusY = s * 0.48 };
            return new CombinedGeometry(GeometryCombineMode.Intersect, sliver, corner);
        }

        /// <summary>WPF NavRail.cs:221: press travels; handled release springs after navigation.</summary>
        private static void HookNavPress(NavSectionRow row)
        {
            var btn = row.Button;
            btn.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                row.Pressed = true;
                SettleNavTilt(row);
                PaintNavCoin(row);
            }, RoutingStrategies.Tunnel);
            btn.AddHandler(PointerReleasedEvent, (_, _) =>
            {
                if (!row.Pressed) return;
                row.Pressed = false;
                row.Hovered = btn.IsPointerOver;
                PaintNavCoin(row, spring: true);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        /// <summary>WPF PaintNavCoin (:384): paints a coin for its row's state, eased by DepthLaw.</summary>
        private static void PaintNavCoin(NavSectionRow row, bool spring = false)
        {
            if (row.Coin == null) return;
            try
            {
                int full = row.Pressed ? DepthLaw.PressMs : spring ? DepthLaw.ReleaseMs : DepthLaw.HoverMs;
                ApplyNavCoinState(row.Coin, row.Lift, row.Pressed, row.Active, row.Hovered,
                    DepthLaw.Ms(full, AmbientFxCanvas.Env.Level), spring);
            }
            catch (Exception ex) { Log.Debug("PaintNavCoin: {E}", ex.Message); }
        }

        /// <summary>WPF NavRail.cs:402: swap seated lighting, ease travel and shadow; ms 0 snaps.</summary>
        internal static void ApplyNavCoinState(NavCoinParts coin, TranslateTransform lift,
            bool pressed, bool active, bool hovered, int ms, bool spring)
        {
            bool seated = pressed || active;
            coin.Rim.BorderBrush = DepthBrush(seated ? "DepthPressedBevel" : "DepthCoinRim");
            coin.Socket.Opacity = seated ? 1 : 0;

            double travel = DepthLaw.TravelFor(true, pressed, active, hovered);
            double shadow = DepthLaw.ShadowFor(true, pressed, active, hovered);
            double discTo = shadow > 0 ? 1 : 0;

            if (ms <= 0)
            {
                lift.Transitions = null;
                coin.DiscShift.Transitions = null;
                coin.Disc.Transitions = null;
                lift.Y = travel;
                if (shadow > 0) coin.DiscShift.Y = shadow;
                coin.Disc.Opacity = discTo;
                return;
            }

            var span = TimeSpan.FromMilliseconds(ms);
            double from = lift.Y;
            Easing ease = spring && Math.Abs(from - travel) > 0.01
                ? new NavSpringEase((CoinOvershoot(from, travel) - from) / (travel - from))
                : new QuadraticEaseOut();
            ReuseNavTransition(lift, TranslateTransform.YProperty, span, ease);
            lift.Y = travel;
            if (shadow > 0)
            {
                ReuseNavTransition(coin.DiscShift, TranslateTransform.YProperty, span, new QuadraticEaseOut());
                coin.DiscShift.Y = shadow;
            }
            ReuseNavTransition(coin.Disc, Visual.OpacityProperty, span, new LinearEasing());
            coin.Disc.Opacity = discTo;
        }

        /// <summary>WPF's release keyframes as one curve: ease out to the overshoot point at 60% of
        /// the time, then ease in-out back to the target. <c>peak</c> is the overshoot as progress.</summary>
        internal sealed class NavSpringEase(double peak) : Easing
        {
            public override double Ease(double progress)
            {
                if (progress <= 0) return 0;
                if (progress >= 1) return 1;
                if (progress < 0.6)
                {
                    double u = progress / 0.6;
                    return (1 - (1 - u) * (1 - u)) * peak;
                }
                double v = (progress - 0.6) / 0.4;
                double io = v < 0.5 ? 2 * v * v : 1 - 2 * (1 - v) * (1 - v);
                return peak + (1 - peak) * io;
            }
        }

        /// <summary>WPF TiltNavCoin (:447): the hover lean toward the pointer, idle coins only.</summary>
        private static void TiltNavCoin(NavSectionRow row, PointerEventArgs e)
        {
            try
            {
                if (row.Face == null || row.Tile == null) return;
                if (row.Pressed || !CoinTilts(row.Active, AmbientFxCanvas.Env.Level, AmbientFxCanvas.Env.CurrentTier))
                {
                    SettleNavTilt(row);
                    return;
                }
                double w = row.Tile.Bounds.Width, h = row.Tile.Bounds.Height;
                if (w <= 0 || h <= 0) return;
                var at = e.GetPosition(row.Tile);
                SetNavTilt(row, CoinTilt(at.X / w * 2 - 1, at.Y / h * 2 - 1), DepthLaw.Ms(CoinTiltMs, AmbientFxCanvas.Env.Level), null);
            }
            catch (Exception ex) { Log.Debug("TiltNavCoin: {E}", ex.Message); }
        }

        /// <summary>WPF SettleNavTilt (:468): back to level over HoverMs (Off snaps).</summary>
        private static void SettleNavTilt(NavSectionRow row) =>
            SetNavTilt(row, 0, DepthLaw.Ms(DepthLaw.HoverMs, AmbientFxCanvas.Env.Level), new QuadraticEaseOut());
        internal static void SetNavTilt(NavSectionRow row, double angle, int ms, Easing? ease)
        {
            if (ms <= 0) row.Tilt.Transitions = null;
            else ReuseNavTransition(row.Tilt, RotateTransform.AngleProperty, TimeSpan.FromMilliseconds(ms), ease ?? new LinearEasing());
            row.Tilt.Angle = angle;
        }
        // WPF NavRail.cs:426-441,463,477 continues from the current animated value.
        // Keeping the transition lets Avalonia capture that value before disposing its old animation.
        private static void ReuseNavTransition(Animatable target, AvaloniaProperty property, TimeSpan duration, Easing ease)
        {
            target.Transitions ??= new Transitions { new DoubleTransition { Property = property } };
            var transition = (DoubleTransition)target.Transitions[0];
            transition.Duration = duration;
            transition.Easing = ease;
        }
    }
}
