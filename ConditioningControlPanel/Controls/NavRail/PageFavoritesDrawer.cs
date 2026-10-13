using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The FAVORITES + RECENT drawer on every page but Home (owner, 2026-10-13). Home keeps its
    /// own drawer inside SettingsTabView; this is the same thing for the other pages: a 22 px
    /// handle on the page's right edge and a 92 px body that PUSHES the page (its own column in
    /// MainWindow's PageAreaGrid), never floats over it.
    ///
    /// <para>It owns no list rules and builds no chips: MainWindow fills <see cref="FavoritesList"/>
    /// and <see cref="RecentList"/> with the same BuildRailChip chips, from the same settings
    /// (RailFavorites, RailRecent), so pins, order and the right-click menu cannot drift from
    /// Home. It persists nothing: it starts closed and closes on every navigation.</para>
    ///
    /// <para>Code-only on purpose (no namescope, realizable in a render test). The slide is the
    /// Home drawer's (200 ms ease-out both ways) and obeys MotionFx; no Effect anywhere.</para>
    /// </summary>
    public sealed class PageFavoritesDrawer : Grid
    {
        internal const double HandleColumn = 22;
        internal const double BodyWidth = 92;
        internal const int SlideMs = 200;

        internal Button Handle { get; }
        internal TextBlock Chevron { get; }
        internal Grid Body { get; }
        internal Border Rail { get; }
        internal StackPanel FavoritesList { get; } = new();
        internal StackPanel RecentList { get; } = new();
        internal TextBlock FavoritesEmpty { get; }
        internal TextBlock RecentEmpty { get; }
        internal TextBlock FavoritesCaption { get; }
        internal TextBlock RecentCaption { get; }
        internal bool IsOpen { get; private set; }

        private readonly Border _handleFace;
        private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);

        /// <summary>The drawer shows on every section page except Home, which has its own.</summary>
        internal static bool ShowsOn(string? section) =>
            !string.IsNullOrEmpty(section) && !string.Equals(section, NavSections.Home, StringComparison.Ordinal);

        public PageFavoritesDrawer()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HandleColumn) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Chevron = new TextBlock
            {
                Text = "‹", FontSize = 16, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 7),
            };
            var star = new TextBlock
            {
                Text = "★", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
                FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
            };
            var word = new TextBlock
            {
                FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new RotateTransform(-90),
            };
            Bind(word, TextBlock.TextProperty, "rail_favorites_drawer");
            var face = new Grid();
            face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            face.RowDefinitions.Add(new RowDefinition());
            face.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetRow(word, 1); SetRow(Chevron, 2);
            face.Children.Add(star); face.Children.Add(word); face.Children.Add(Chevron);
            _handleFace = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = face };

            // The face is the button's whole template: the Border above paints it, so hover is
            // a repaint of two brushes (in and out), never an Effect.
            var template = new ControlTemplate(typeof(Button));
            template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            Handle = new Button
            {
                Template = template, Content = _handleFace, Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(2, 5, 2, 5), FocusVisualStyle = null,
            };
            Bind(Handle, ToolTipProperty, "tooltip_rail_favorites_drawer");
            Bind(Handle, AutomationProperties.NameProperty, "rail_favorites_drawer");
            Handle.Click += (_, _) => SetOpen(!IsOpen, animate: true);
            Handle.MouseEnter += (_, _) => PaintHandle();
            Handle.MouseLeave += (_, _) => PaintHandle();
            Handle.GotKeyboardFocus += (_, _) => PaintHandle();
            Handle.LostKeyboardFocus += (_, _) => PaintHandle();
            Children.Add(Handle);

            FavoritesCaption = Caption("rail_favorites_caption");
            RecentCaption = Caption("rail_recent_caption");
            RecentCaption.Margin = new Thickness(0, 8, 0, 6);
            FavoritesEmpty = Caption("rail_favorites_empty");
            RecentEmpty = Caption("rail_recent_empty");
            // Width 69 = the Home rail's chip width, so one chip style fits both drawers.
            var stack = new StackPanel { Margin = new Thickness(4, 10, 4, 10), Width = 69, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var child in new UIElement[] { FavoritesCaption, FavoritesList, FavoritesEmpty, RecentCaption, RecentList, RecentEmpty })
                stack.Children.Add(child);
            Rail = new Border
            {
                Width = BodyWidth - 5, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 5, 5),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack,
                },
            };
            Rail.SetResourceReference(Border.BackgroundProperty, "ElevatedSurfaceBrush");
            // Pinned at 87 and right-aligned inside a clipped, growing cell: the body slides in
            // from the edge instead of squeezing the chips.
            Body = new Grid { Width = 0, ClipToBounds = true };
            Body.Children.Add(Rail);
            SetColumn(Body, 1);
            Children.Add(Body);
            PaintHandle();
        }

        private static TextBlock Caption(string key)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            Bind(t, TextBlock.TextProperty, key);
            return t;
        }

        private static void Bind(DependencyObject target, DependencyProperty property, string key) =>
            BindingOperations.SetBinding(target, property, new Binding($"[{key}]") { Source = LocalizationManager.Instance });

        /// <summary>The handle and the rail border wear the active mod's glow colour, as on Home.</summary>
        internal void Paint(Color hue)
        {
            _hue = hue;
            Rail.BorderBrush = Frozen(hue, 0x80);
            PaintHandle();
        }

        private void PaintHandle()
        {
            bool lit = Handle.IsMouseOver || Handle.IsKeyboardFocused;
            _handleFace.Background = Frozen(_hue, lit ? (byte)0x4D : (byte)0x26);
            _handleFace.BorderBrush = Frozen(_hue, lit ? (byte)0xFF : (byte)0x80);
            var ink = Frozen(Views.Tabs.SettingsTabView.Lighten(_hue, lit ? 0.55 : 0.18), 0xFF);
            foreach (var child in ((Grid)_handleFace.Child).Children)
                if (child is TextBlock t) t.Foreground = ink;
        }

        private static SolidColorBrush Frozen(Color c, byte alpha)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        /// <summary>Open or close the body. The chevron points the way a click goes.</summary>
        internal void SetOpen(bool open, bool animate)
        {
            IsOpen = open;
            Chevron.Text = open ? "›" : "‹";
            double to = open ? BodyWidth : 0;
            double from = Body.Width;   // the animated value while a slide is running
            Body.Width = to;
            if (!animate || !Services.MotionFx.AllowTransitions || !IsVisible || double.IsNaN(from) || from == to)
            {
                Body.BeginAnimation(WidthProperty, null);
                return;
            }
            Body.BeginAnimation(WidthProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(SlideMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        }

        /// <summary>A navigation landed on <paramref name="section"/>: the drawer shows or hides
        /// for that page and is always put away (no slide: the page under it just changed).</summary>
        internal void OnNavigated(string? section)
        {
            SetOpen(false, animate: false);
            Visibility = ShowsOn(section) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
