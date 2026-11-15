using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The cut corner on a dashboard tile (owner, 2026-11-15): the switch for the feature's V2.
    /// Gold when on, a dark dog-ear with a gold edge when off. It follows the tile's own click
    /// rule (the host routes the clicks): the "on/off" click flips the V2, the other click opens
    /// the feature's settings like the rest of the tile.
    /// </summary>
    public sealed class SuperCorner : FrameworkElement
    {
        /// <summary>Length of the two short sides, DIPs.</summary>
        public const double Leg = 32;

        private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
        private static readonly Brush GoldBrush = Frozen(Gold);
        private static readonly Brush GoldHot = Frozen(Color.FromRgb(0xFF, 0xE0, 0x9A));
        private static readonly Brush OffBrush = Frozen(Color.FromArgb(0xE6, 0x1A, 0x1A, 0x2E));
        private static readonly Brush OffHot = Frozen(Color.FromArgb(0xF2, 0x3A, 0x30, 0x52));
        private static readonly Brush Ink = Frozen(Color.FromRgb(0x1A, 0x10, 0x30));
        private static readonly Brush DimStar = Frozen(Color.FromArgb(0xB0, 0xFF, 0xCF, 0x6B));
        private static readonly Pen Edge = FrozenPen(Color.FromArgb(0xC0, 0xFF, 0xCF, 0x6B), 1.2);
        private static readonly Typeface Face = new(new FontFamily("Segoe UI Symbol, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double w) { var p = new Pen(Frozen(c), w); p.Freeze(); return p; }

        public SuperEffect Effect { get; }
        /// <summary>False = the top-left corner, true = the bottom-right one.</summary>
        public bool BottomRight { get; }
        /// <summary>Does the host's "on/off" click sit on the LEFT button right now (clicks inverted).</summary>
        public Func<bool> Inverted { get; set; } = () => false;

        public SuperCorner(SuperEffect effect, bool bottomRight)
        {
            Effect = effect;
            BottomRight = bottomRight;
            Width = Height = Leg;
            HorizontalAlignment = bottomRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            VerticalAlignment = bottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            Cursor = Cursors.Hand;
            Panel.SetZIndex(this, 12);
            ToolTip = new ToolTip();
            ToolTipService.SetInitialShowDelay(this, 350);
            ToolTipService.SetShowDuration(this, 20000);
            ToolTipOpening += (_, _) => ToolTip = BuildTip();
            MouseEnter += (_, _) => InvalidateVisual();
            MouseLeave += (_, _) => InvalidateVisual();
            Loaded += (_, _) =>
            {
                SuperAccess.HookTierEvents();
                SuperAccess.Changed += OnChanged;
                InvalidateVisual();
            };
            Unloaded += (_, _) => SuperAccess.Changed -= OnChanged;
        }

        /// <summary>Is the V2 drawn as on: unlocked and switched on.</summary>
        public bool Lit => SuperAccess.IsUnlocked(Effect) && SuperAccess.IsSwitchedOn(Effect);

        private void OnChanged(SuperEffect e)
        {
            if (e == Effect) Dispatcher.BeginInvoke(new Action(InvalidateVisual));
        }

        /// <summary>The "on/off" click landed on the corner.</summary>
        public void Press()
        {
            SuperDoor.Press(Effect);
            InvalidateVisual();
        }

        private Geometry Shape()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                if (BottomRight)
                {
                    c.BeginFigure(new Point(Leg, Leg), true, true);
                    c.LineTo(new Point(0, Leg), true, false);
                    c.LineTo(new Point(Leg, 0), true, false);
                }
                else
                {
                    c.BeginFigure(new Point(0, 0), true, true);
                    c.LineTo(new Point(Leg, 0), true, false);
                    c.LineTo(new Point(0, Leg), true, false);
                }
            }
            g.Freeze();
            return g;
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters p)
        {
            var pt = p.HitPoint;
            bool inside = BottomRight ? pt.X + pt.Y >= Leg : pt.X + pt.Y <= Leg;
            return inside && pt.X >= 0 && pt.Y >= 0 && pt.X <= Leg && pt.Y <= Leg ? new PointHitTestResult(this, pt) : null;
        }

        protected override void OnRender(DrawingContext dc)
        {
            bool lit = Lit, hot = IsMouseOver;
            dc.DrawGeometry(lit ? (hot ? GoldHot : GoldBrush) : (hot ? OffHot : OffBrush), null, Shape());
            // The cut edge: the long side, so an off corner still reads as a gold dog-ear.
            dc.DrawLine(Edge, BottomRight ? new Point(0, Leg) : new Point(Leg, 0), BottomRight ? new Point(Leg, 0) : new Point(0, Leg));

            var star = new FormattedText("★", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 12.5,
                lit ? Ink : DimStar, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double c = BottomRight ? Leg * 0.69 : Leg * 0.31;
            dc.DrawText(star, new Point(c - star.Width / 2, c - star.Height / 2));
        }

        private ToolTip BuildTip()
        {
            var panel = new StackPanel { MaxWidth = 250 };
            panel.Children.Add(new TextBlock
            {
                Text = Loc.GetF("super_corner_title", SuperNames.Name(Effect)), FontWeight = FontWeights.SemiBold, Foreground = GoldBrush, FontSize = 12.5,
            });
            panel.Children.Add(new TextBlock
            {
                Text = SuperDoor.Twist(Effect), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Opacity = 0.85,
                FontSize = 11.5, Margin = new Thickness(0, 3, 0, 5),
            });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Get(Inverted() ? "super_corner_tip_inverted" : "super_corner_tip"), TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White, Opacity = 0.6, FontSize = 11,
            });
            if (!SuperAccess.IsUnlocked(Effect))
                panel.Children.Add(new TextBlock
                {
                    Text = Loc.Get("super_for_supporters"), Foreground = GoldBrush, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
                });
            return new ToolTip
            {
                Content = panel, Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1A, 0x1A, 0x2E)),
                BorderBrush = GoldBrush, BorderThickness = new Thickness(1),
            };
        }
    }
}
