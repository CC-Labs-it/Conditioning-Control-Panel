using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The run-pick ribbon, the Avalonia twin of WPF <c>ConditioningControlPanel/Chaos/ChaosBoonBarOverlay.cs</c>:
    /// one tile per mantra/sin drafted during the descent (<see cref="ChaosRunState.RunPickTiles"/>),
    /// in pick order, top-right beside the wave-timer clock, growing leftward. Rules (order, pop-in,
    /// placement) live in <see cref="ChaosBoonBarRules"/>. <see cref="ChaosRunHost"/> creates it at
    /// BeginRun, rebuilds it on every pick and closes it at run end / panic, as WPF ChaosModeService
    /// does (:417-420, :3182, :3235, :3322).
    ///
    /// <para>Not click-through, as in WPF (:110): hovering a tile shows its card. No click handlers,
    /// ShowActivated=false (WS_EX_NOACTIVATE), ShowInTaskbar=false (WS_EX_TOOLWINDOW).</para>
    /// <para>ponytail: WPF RaiseActive re-stacks it above a chaos mandatory video (ChaosWindowZ);
    /// this head plays no chaos video yet, so Topmost alone is the whole contract here.</para>
    /// </summary>
    internal sealed class ChaosBoonBarOverlay : Window
    {
        private static readonly IBrush PillBack = new ImmutableSolidColorBrush(Color.FromArgb(150, 0x12, 0x0E, 0x1E));
        private static readonly IBrush PillBorder = new ImmutableSolidColorBrush(Color.FromArgb(140, 0xE8, 0x43, 0x93));
        private static readonly IBrush TipBack = new ImmutableSolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x12, 0x26));
        private static readonly IBrush TipBorder = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4));
        private static readonly IBrush DescFore = new ImmutableSolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xF0));
        private static readonly IBrush FlavorFore = new ImmutableSolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8));

        private readonly Border _pill;
        private readonly StackPanel _row;
        private readonly List<(Control Tile, ScaleTransform Scale, int Order)> _popping = new();
        private readonly BeatLoop _popBeat;   // the window's shared 30 fps beat, only while tiles pop
        private int _shownCount;   // tiles on the ribbon at the last rebuild: newcomers pop in (WPF :145)

        internal StackPanel Row => _row;
        internal Border Pill => _pill;

        /// <summary>The --render-all constructor: a settled ribbon of the pool's first mantra and first sin,
        /// drafted through the real <see cref="ChaosRunState.ApplyBoon"/> so the tiles are what a run builds.</summary>
        internal ChaosBoonBarOverlay() : this(renderSample: true)
        {
            var state = new ChaosRunState(new ChaosRunConfig());
            state.ApplyBoon(ChaosBoonPool.All.Find(b => !b.IsCurse)!);
            state.ApplyBoon(ChaosBoonPool.All.Find(b => b.IsCurse)!);
            SetPicks(state.RunPickTiles);
            StepPops(double.MaxValue);
        }

        /// <summary>WPF EnsureCreated (:34): the empty, pill-collapsed window a run starts with.</summary>
        internal static ChaosBoonBarOverlay ForRun() => new(renderSample: false);

        private ChaosBoonBarOverlay(bool renderSample)
        {
            _ = renderSample;   // only selects the constructor; the sample lives in the render ctor body
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;   // WPF ChaosWindowZ.BornTopmost, as the HUD (:189)
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;
            // Never 0x0 while the pill is collapsed (no picks yet): a zero-size window stalls the compositor
            // for every window (headless: other windows stop hit-testing) and is invalid on X11.
            MinWidth = 1; MinHeight = 1;

            _row = new StackPanel { Orientation = Orientation.Horizontal };
            _pill = new Border
            {
                Background = PillBack,
                BorderBrush = PillBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 6, 10, 6),
                IsVisible = false,
                Child = _row,
                // The window is SizeToContent, so these only matter in a fixed frame (--render-all):
                // there the pill sits top-right, where a run puts it.
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Content = _pill;

            // WPF ToolTip chrome (:241-247): non-layered card, pink hairline.
            Styles.Add(new Style(x => x.OfType<ToolTip>())
            {
                Setters =
                {
                    new Setter(TemplatedControl.BackgroundProperty, TipBack),
                    new Setter(TemplatedControl.BorderBrushProperty, TipBorder),
                    new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                    new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 8, 10, 8)),
                },
            });

            _popBeat = new BeatLoop(this, sec => StepPops(sec * 1000));
            Opened += (_, _) => Place();
            SizeChanged += (_, _) => Place();
            Closed += (_, _) => StopPops();
        }

        /// <summary>Right-anchored left of the clock, 10 DIPs below the work area top (WPF :129-133).</summary>
        private void Place()
        {
            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            if (screen == null) return;
            var wa = screen.WorkingArea;
            double scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            double left = ChaosBoonBarRules.Left(wa.Right / scale, Bounds.Width);
            Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(wa.Y + ChaosBoonBarRules.TopInset * scale));
        }

        /// <summary>Rebuild the ribbon from the run's picks (WPF SetPicks + Rebuild :49, :144). UI thread.</summary>
        public void SetPicks(IEnumerable<ChaosSidebarBoon> picks)
        {
            var snapshot = ChaosBoonBarRules.Snapshot(picks);
            StopPops();
            _row.Children.Clear();
            if (snapshot.Count == 0) { _pill.IsVisible = false; _shownCount = 0; return; }
            _pill.IsVisible = true;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var tile = BuildTile(snapshot[i]);
                _row.Children.Add(tile);
                int order = ChaosBoonBarRules.PopOrder(i, _shownCount);
                if (order < 0) continue;
                var st = new ScaleTransform(ChaosBoonBarRules.PopFrom, ChaosBoonBarRules.PopFrom);
                tile.RenderTransformOrigin = RelativePoint.Center;   // WPF (0.5, 0.5)
                tile.RenderTransform = st;
                tile.Opacity = 0;
                _popping.Add((tile, st, order));
            }
            _shownCount = snapshot.Count;
            if (_popping.Count == 0) return;
            _popBeat.Start();
        }

        /// <summary>One pop frame at <paramref name="elapsedMs"/> since the rebuild (P54 manual-frame seam).</summary>
        internal void StepPops(double elapsedMs)
        {
            foreach (var (tile, st, order) in _popping)
            {
                var (s, o) = ChaosBoonBarRules.PopFrame(order, elapsedMs);
                st.ScaleX = st.ScaleY = s;
                tile.Opacity = o;
            }
            if (ChaosBoonBarRules.PopDone(_popping.Count, elapsedMs)) StopPops();
        }

        private void StopPops()
        {
            _popBeat.Stop();
            _popping.Clear();
        }

        private static Control BuildTile(ChaosSidebarBoon b)
        {
            const double t = ChaosBoonBarRules.Tile;
            var accent = ChaosBoonColors.SidebarAccent(b);
            var inner = new Panel();
            if (b.Icon is IImage img)
                inner.Children.Add(new Image { Source = img, Stretch = Stretch.UniformToFill });
            else
                inner.Children.Add(new IconGlyph
                {
                    Emoji = b.Glyph, Size = 19, Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
            inner.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), BorderBrush = accent, BorderThickness = new Thickness(3), IsHitTestVisible = false,
            });
            var tile = new Border
            {
                Width = t, Height = t,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Background = ChaosBoonColors.SidebarBack(b),
                Margin = new Thickness(0, 0, ChaosBoonBarRules.TileGap, 0),
                Child = inner,
            };
            ToolTip.SetTip(tile, BuildTip(b, accent));
            return tile;
        }

        /// <summary>The hover card: name (accent, bold), desc, italic flavour (WPF BuildTip :213).</summary>
        private static Control BuildTip(ChaosSidebarBoon b, IBrush accent)
        {
            var panel = new StackPanel { MaxWidth = 240 };
            panel.Children.Add(new TextBlock { Text = b.Name, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = accent });
            if (b.HasDesc)
                panel.Children.Add(new TextBlock
                {
                    Text = b.Desc, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Foreground = DescFore,
                });
            if (b.HasFlavor)
                panel.Children.Add(new TextBlock
                {
                    Text = b.Flavor, FontStyle = FontStyle.Italic, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0), Foreground = FlavorFore,
                });
            return panel;
        }
    }
}
