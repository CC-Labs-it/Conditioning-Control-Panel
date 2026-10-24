using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/EnhancementsTabView.xaml.cs plus the drawing
    /// half of MainWindow.Enhancements.cs (RefreshEnhancementsUI / DrawSkillTree /
    /// CreateSkillTreeHeader / CreateSkillNode / PopulateSecretSkills) and the node FX of
    /// MainWindow.EnhancementsFx.cs (owned-node breath, hover pop) and the drifting gradients of
    /// CreateAnimatedSkillTreeBrush. Ownership, purchasability, multiplier and time come from Core
    /// <c>SkillTreeRules</c>, the same rules WPF SkillTreeService delegates to.
    ///
    /// <para>Purchasing (SkillCard_Click) and its bursts live in EnhancementsTabView.Purchase.cs; PRO analytics in
    /// EnhancementsTabView.Analytics.cs.</para>
    /// </summary>
    public partial class EnhancementsTabView : UserControl
    {
        /// <summary>Node box, verbatim from MainWindow.Enhancements.cs (NodeWidth/NodeHeight).</summary>
        private const double NodeWidth = 156, NodeHeight = 139;

        /// <summary>Rail card box, verbatim from MainWindow.Enhancements.cs.</summary>
        private const double SecretCardWidth = 180, SecretCardHeight = 56;

        // MainWindow.EnhancementsFx.cs tuning, verbatim.
        private const double OwnedNodeGlowMinOpacity = 0.38, OwnedNodeGlowMaxOpacity = 0.72, OwnedNodeGlowSeconds = 3.8;
        private const double SkillNodeHoverScale = 1.25;
        private const int SkillNodeHoverInMs = 250, SkillNodeHoverOutMs = 200;

        /// <summary>The FX clock. Tests swap in a stepped clock and call <see cref="StepFx"/>.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        private readonly List<Border> _ownedGlows = new();
        private readonly Dictionary<string, Bitmap?> _blurredArt = new();
        private readonly Dictionary<string, Bitmap?> _art = new();
        private LinearGradientBrush? _treeBrush, _headerBrush;
        private BeatLoop? _fxTimer;
        private long _fxStarted;
        private IDisposable? _visibilityWatch;
        private Window? _window;
        private System.ComponentModel.INotifyPropertyChanged? _settings;

        public EnhancementsTabView()
        {
            InitializeComponent();

            // Tunneling, like WPF's Preview- pair, so the redirect wins before the ScrollViewer's
            // own handler consumes the wheel.
            SkillTreeScroller.AddHandler(PointerWheelChangedEvent, OnSkillTreeWheel, RoutingStrategies.Tunnel);

            Repaint();
        }

        /// <summary>True while the tab's one FX clock (breath + gradient drift) runs. Test seam.</summary>
        internal bool FxRunning => _fxTimer?.IsRunning == true;

        /// <summary>The owned nodes' glows the breath drives. Test seam.</summary>
        internal IReadOnlyList<Border> OwnedGlows => _ownedGlows;

        /// <summary>Locked art blurred once per skill. Test seam.</summary>
        internal IReadOnlyDictionary<string, Bitmap?> BakedArt => _blurredArt;

        // WPF repaints from SkillTreeService events and on every ShowTab; here AppSettings
        // PropertyChanged, the tab becoming visible, a mod switch and a language switch.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _settings = CoreSettings.Current;
            _settings.PropertyChanged += OnSettingsChanged;
            CoreMods.ModChanged += OnModChanged;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            AmbientFxCanvas.Env.MotionGateChanged += EvaluateFx;
            _visibilityWatch = EffectiveVisibility.Watch(this, OnVisibilityChanged);
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window != null) _window.PropertyChanged += OnWindowPropertyChanged;
            // The ctor already painted; repaint only what changed while detached.
            if (_dirty) Repaint(); else EvaluateFx();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
            CoreMods.ModChanged -= OnModChanged;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            AmbientFxCanvas.Env.MotionGateChanged -= EvaluateFx;
            _visibilityWatch?.Dispose();
            _visibilityWatch = null;
            if (_window != null) _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
            StopFx(rest: false);
            ParkSheen();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppSettings.SkillPoints) or nameof(AppSettings.UnlockedSkills) or nameof(AppSettings.PinkRushActive))
                Dispatcher.UIThread.Post(RepaintIfShown);
        }

        private void OnModChanged(object? sender, ModPackage e) => Dispatcher.UIThread.Post(() => { _art.Clear(); _blurredArt.Clear(); RepaintIfShown(); });

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RepaintIfShown);

        /// <summary>A hidden or detached tab repaints on its next show/attach (P07; WPF's sweep
        /// and SparkleWallet refresh only the visible tab).</summary>
        private void RepaintIfShown()
        {
            // Mid-purchase the bought node must stay attached until its burst fires; BuyAsync repaints after.
            if (!_buying && IsEffectivelyVisible && VisualRoot != null) Repaint();
            else _dirty = true;
        }

        private bool _dirty;

        private void OnVisibilityChanged()
        {
            if (IsEffectivelyVisible) Repaint();
            else { EvaluateFx(); ParkSheen(); }
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.IsActiveProperty || e.Property == Window.WindowStateProperty) EvaluateFx();
        }

        /// <summary>RefreshEnhancementsUI: hidden named fields, tree, secret rail, active bonuses.</summary>
        private void Repaint()
        {
            _dirty = false;
            var settings = CoreSettings.Current;
            var multiplier = SkillTreeRules.GetTotalXpMultiplier(settings, DateTime.Now.Hour, 0.0);
            TxtSkillPoints.Text = settings.SkillPoints.ToString("N0");
            TxtXpMultiplier.Text = $"{multiplier:F2}x";
            TxtConditioningTime.Text = SkillTreeRules.FormatConditioningTime(settings);
            TxtPinkRushIndicator.IsVisible = settings.PinkRushActive;

            SkillTreeCanvas.Children.Clear();
            SecretSkills.Children.Clear();
            _ownedGlows.Clear();
            _treeBrush = TreeBrush(header: false);
            SkillTreeOuterBorder.Background = _treeBrush;
            PaintTree();
            PaintSecretRail();
            RefreshActiveBonuses();
            _fxStarted = Time.GetTimestamp();
            StopFx(rest: false);
            EvaluateFx();
        }

        /// <summary>
        /// Redirects vertical wheel to horizontal scrolling for the skill tree — EXCEPT when the
        /// wheel is over a nested vertically-scrollable region (the tree-header panel with its
        /// stats/analytics expanders). There we yield so the inner viewer scrolls vertically;
        /// otherwise its content below the canvas fold would be unreachable.
        ///
        /// <para>The step is 60px, not WPF's <c>Delta * 0.5</c>: WPF reports ±120 per notch and
        /// Avalonia reports ±1, so keeping the multiplier would have scrolled half a pixel.</para>
        /// </summary>
        private void OnSkillTreeWheel(object? sender, PointerWheelEventArgs e)
        {
            const double stepPerNotch = 60;

            for (var el = e.Source as Visual; el is not null && el != SkillTreeScroller; el = el.GetVisualParent())
            {
                if (el is ScrollViewer inner && inner.Extent.Height > inner.Viewport.Height)
                    return; // let the inner viewer take the wheel (vertical scroll)
            }

            var offset = SkillTreeScroller.Offset;
            SkillTreeScroller.Offset = new Vector(offset.X - e.Delta.Y * stepPerNotch, offset.Y);
            e.Handled = true;
        }

        // =====================================================================================
        //  FX: owned-node breath + the two drifting gradients, one clock (EnhancementsFx.cs)
        // =====================================================================================

        /// <summary>WPF EnhancementsAmbientAllowed AND EnhancementsFxOnScreen. Off/Reduced or the
        /// Performance tier rests the art; hidden tab, inactive or minimised window parks it.</summary>
        private void EvaluateFx()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowAmbientLoops) { StopFx(rest: true); return; }
                bool onScreen = IsEffectivelyVisible && _window is { IsActive: true } w && w.WindowState != WindowState.Minimized;
                if (!onScreen) { StopFx(rest: false); return; }
                // The window's shared 30 fps beat (WPF AmbientFrameRate 30, MainWindow.EnhancementsFx.cs:186); Start ticks once.
                _fxTimer ??= new BeatLoop(this, _ => Tick());
                _fxTimer.Start();
            }
            catch (Exception ex) { Serilog.Log.Debug("Enhancements EvaluateFx: {E}", ex.Message); }
        }

        private void StopFx(bool rest)
        {
            _fxTimer?.Stop();
            if (rest) Apply(0, rested: true);
        }

        /// <summary>One clock tick at <see cref="Time"/>'s now; a no-op while the clock is stopped.</summary>
        internal void StepFx() { if (FxRunning) Tick(); }

        private void Tick() => Apply(Time.GetElapsedTime(_fxStarted).TotalSeconds, rested: false);

        /// <summary>Samples every loop at <paramref name="t"/> seconds. AutoReverse + Forever + SineEase
        /// EaseInOut, as the WPF DoubleAnimation/ColorAnimation pairs.</summary>
        private void Apply(double t, bool rested)
        {
            static double P(double t, double dur)
            {
                var c = t % (2 * dur) / dur;
                var x = c <= 1 ? c : 2 - c;
                return (1 - Math.Cos(Math.PI * x)) / 2;
            }
            static Color Mix(Color a, Color b, double p) => Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * p), (byte)(a.G + (b.G - a.G) * p), (byte)(a.B + (b.B - a.B) * p));

            var glow = rested ? 0.6 : OwnedNodeGlowMinOpacity + (OwnedNodeGlowMaxOpacity - OwnedNodeGlowMinOpacity) * P(t, OwnedNodeGlowSeconds);
            foreach (var g in _ownedGlows) g.Opacity = glow;

            if (_headerBrush is { GradientStops.Count: 3 } h)
            {
                h.GradientStops[1].Offset = rested ? 0.5 : 0.2 + 0.6 * P(t, 5);
                h.GradientStops[1].Color = rested ? Color.FromRgb(80, 30, 100) : Mix(Color.FromRgb(80, 30, 100), Color.FromRgb(120, 40, 90), P(t, 4));
            }
            if (_treeBrush is { GradientStops.Count: 4 } b)
            {
                b.GradientStops[1].Offset = rested ? 0.3 : 0.15 + 0.35 * P(t, 6);
                b.GradientStops[2].Offset = rested ? 0.7 : 0.5 + 0.35 * P(t, 8);
                b.GradientStops[1].Color = rested ? Color.FromRgb(60, 25, 80) : Mix(Color.FromRgb(60, 25, 80), Color.FromRgb(35, 40, 90), P(t, 7));
            }
        }

        /// <summary>CreateAnimatedSkillTreeBrush's stops, at rest; the clock drifts them.</summary>
        private static LinearGradientBrush TreeBrush(bool header)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            };
            void Stop(byte r, byte g, byte b, double o) => brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), o));
            if (header) { Stop(35, 20, 60, 0); Stop(80, 30, 100, 0.5); Stop(35, 20, 60, 1); }
            else { Stop(25, 15, 50, 0); Stop(60, 25, 80, 0.3); Stop(30, 35, 75, 0.7); Stop(25, 15, 50, 1); }
            return brush;
        }

        /// <summary>ApplySkillNodeHover: the 1.25 pop plus the z-order lift; Off snaps.</summary>
        internal static void ApplyNodeHover(Control node, bool on)
        {
            node.ZIndex = on ? 10 : 0;
            node.Transitions = AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions
                {
                    new TransformOperationsTransition
                    {
                        Property = RenderTransformProperty,
                        Duration = TimeSpan.FromMilliseconds(on ? SkillNodeHoverInMs : SkillNodeHoverOutMs),
                        Easing = on ? new BackEaseOut() : new QuadraticEaseInOut(),
                    },
                }
                : null;
            node.RenderTransform = TransformOperations.Parse(on ? $"scale({SkillNodeHoverScale.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "scale(1)");
        }

        // =====================================================================================
        //  DrawSkillTree / PopulateSecretSkills
        // =====================================================================================

        /// <summary>DrawSkillTree's layout, verbatim: header at x=5, root at 570, columns 270
        /// apart, the three paths 160 apart, the analytics chain on the middle row.</summary>
        internal static Dictionary<string, (double X, double Y)> NodePositions()
        {
            const double startX = 570, colSpacing = 270, rowSpacing = 160;
            var pos = new Dictionary<string, (double X, double Y)> { ["pink_hours"] = (startX, rowSpacing) };
            void Row(double y, int firstCol, params string[] ids)
            {
                for (int i = 0; i < ids.Length; i++) pos[ids[i]] = (startX + colSpacing * (firstCol + i), y);
            }
            Row(0, 1, "ditzy_data", "hive_mind", "trophy_case", "popular_girl", "quest_refresh", "better_quests");
            Row(rowSpacing, 1, "sparkle_boost_1", "sparkle_boost_2", "lucky_bimbo", "sparkle_boost_3", "lucky_bubbles", "pink_rush");
            Row(rowSpacing * 2, 1, "good_girl_streak", "milestone_rewards", "oopsie_insurance", "streak_power", "reroll_addict", "perfect_bimbo_week");
            Row(rowSpacing, 7, "ditzy_data_pro", "season_rewind", "bestie_records", "brain_drain_report", "certified_data_bimbo");
            return pos;
        }

        private void PaintTree()
        {
            var settings = CoreSettings.Current;
            var positions = NodePositions();
            var tree = SkillDefinition.All.Where(s => !s.IsSecret && positions.ContainsKey(s.Id)).ToList();

            // DrawConnectionLines' pairs are exactly each node's prerequisite edge. Lines first,
            // so they sit behind the nodes.
            foreach (var skill in tree.Where(s => s.PrerequisiteId != null && positions.ContainsKey(s.PrerequisiteId)))
            {
                var (px, py) = positions[skill.PrerequisiteId!];
                var (cx, cy) = positions[skill.Id];
                SkillTreeCanvas.Children.AddRange(Connector(px + NodeWidth, py + NodeHeight / 2, cx, cy + NodeHeight / 2,
                    SkillTreeRules.HasSkill(settings, skill.Id), SkillTreeRules.HasSkill(settings, skill.PrerequisiteId!)));
            }

            SkillTreeCanvas.Children.Add(Place(Header(), 5, 0));
            foreach (var skill in tree)
                SkillTreeCanvas.Children.Add(Place(Node(skill), positions[skill.Id].X, positions[skill.Id].Y));
        }

        private static Control Place(Control c, double left, double top)
        {
            Canvas.SetLeft(c, left);
            Canvas.SetTop(c, top);
            return c;
        }

        private static IBrush Accent => new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex));
        private static IBrush AccentLight => new SolidColorBrush(Color.Parse(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1"));
        private static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        /// <summary>DrawConnectionLines: green + glow when the child is owned, accent when only
        /// the parent is.</summary>
        private static Control[] Connector(double x1, double y1, double x2, double y2, bool childOwned, bool parentOwned)
        {
            Line L(IBrush stroke, double thickness, double opacity) => new()
            {
                StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2),
                Stroke = stroke, StrokeThickness = thickness, Opacity = opacity, IsHitTestVisible = false,
                StrokeLineCap = PenLineCap.Round,
            };
            var line = L(childOwned ? Rgb(100, 255, 150) : parentOwned ? Accent : Rgb(60, 60, 80), childOwned ? 3 : 2,
                childOwned || parentOwned ? 1.0 : 0.3);
            line.StrokeLineCap = PenLineCap.Flat;
            if (!childOwned) return new Control[] { line };
            // WPF's LimeGreen DropShadowEffect (blur 8, 0.6) as two soft underlay strokes: an Effect would re-blur
            // every line on each frame the tree's gradient drifts (P82 rule 3).
            return new Control[] { L(Brushes.LimeGreen, 13, 0.12), L(Brushes.LimeGreen, 7, 0.25), line };
        }

        /// <summary>Locked art blurred once (WPF BlurEffect Radius 8 at the node's 156 px width; Skia's
        /// radius-to-sigma 0.57735 r + 0.5, scaled to the bitmap's pixels). Null when it cannot be baked.</summary>
        private Bitmap? BlurredArt(string id, Bitmap src)
        {
            if (_blurredArt.TryGetValue(id, out var done)) return done;
            Bitmap? baked = null;
            try
            {
                using var ms = new System.IO.MemoryStream();
                src.Save(ms);
                ms.Position = 0;
                using var sk = SkiaSharp.SKBitmap.Decode(ms);
                float sigma = (float)((0.57735 * 8 + 0.5) * sk.Width / NodeWidth);
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(sk.Width, sk.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
                using var paint = new SkiaSharp.SKPaint { ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(sigma, sigma) };
                surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
                surface.Canvas.DrawBitmap(sk, 0, 0, paint);
                using var image = surface.Snapshot();
                using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                baked = new Bitmap(png.AsStream());
            }
            catch (Exception ex) { Serilog.Log.Debug("Enhancements blurred art {Id}: {E}", id, ex.Message); }
            return _blurredArt[id] = baked;
        }

        /// <summary>CreateSkillTreeHeader, including the four PRO expanders (AddProSections).</summary>
        private Control Header()
        {
            var settings = CoreSettings.Current;
            var mods = App.Mods;
            var main = new StackPanel();

            var title = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            title.Children.Add(IconRow(Glyph(IconKind.Sparkle, IconVariant.Filled, null, 22, IconBrushes.Gold),
                Text(mods?.GetEnhancementTreeTitle() ?? Loc.Get("label_enhancement_tree_title"), Accent, 22, bold: true)));
            title.Children.Add(Text(mods?.GetEnhancementTreeSubtitle() ?? Loc.Get("label_enhancement_tree_subtitle"), Rgb(176, 176, 176), 11, italic: true, top: 4));
            title.Children.Add(Text(mods?.GetEnhancementTreeWarning() ?? Loc.Get("label_enhancement_tree_warning"), Rgb(136, 170, 204), 10, italic: true, top: 2));
            if (SkillTreeRules.HasSkill(settings, "certified_data_bimbo"))
                title.Children.Add(new Border
                {
                    Background = Rgb(70, 55, 25),
                    BorderBrush = Rgb(255, 200, 80),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = IconRow(Glyph(IconKind.HatGraduation, IconVariant.Regular, Rgb(255, 200, 80), 10),
                        Text(Loc.Get("skill_certified_data_bimbo_name"), Rgb(255, 200, 80), 10, bold: true)),
                });
            main.Children.Add(title);

            // Active bonuses straight under the title (WPF ticket 2026-09-24). Entry 0 is the base.
            var breakdown = SkillTreeRules.GetMultiplierBreakdown(settings, DateTime.Now.Hour, 0.0);
            if (breakdown.Count > 1)
            {
                main.Children.Add(Text(Loc.Get("label_active_bonuses"), Rgb(176, 176, 176), 11, bottom: 6));
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
                foreach (var (source, value) in breakdown.Skip(1))
                    wrap.Children.Add(Chip($"{mods?.MakeModAware(source) ?? source}: +{value:P0}", new Thickness(8, 3, 8, 3), new Thickness(0, 0, 6, 6)));
                main.Children.Add(wrap);
            }

            // Sparkle points.
            var points = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var gem = Glyph(IconKind.Diamond, IconVariant.Filled, null, 24, IconBrushes.Gem);
            gem.Margin = new Thickness(0, 0, 10, 0);
            points.Children.Add(gem);
            var info = new StackPanel();
            info.Children.Add(Text(mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points"), Rgb(176, 176, 176), 10));
            info.Children.Add(Text(settings.SkillPoints.ToString("N0"), Accent, 24, bold: true));
            points.Children.Add(info);
            main.Children.Add(Box(Rgb(42, 42, 74), new Thickness(15, 10, 15, 10), points));

            // Prestige: lifetime points spent, rank 1 + spent / 100.
            var spent = App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0;
            var prestige = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var star = Glyph(IconKind.Sparkle, IconVariant.Filled, Rgb(255, 200, 80), 18);
            star.Margin = new Thickness(0, 0, 10, 0);
            prestige.Children.Add(star);
            var pInfo = new StackPanel();
            pInfo.Children.Add(Text(Loc.Get("label_prestige"), Rgb(176, 176, 176), 10));
            var pValue = new StackPanel { Orientation = Orientation.Horizontal };
            pValue.Children.Add(Text(spent.ToString("N0"), Rgb(255, 200, 80), 16, bold: true));
            var rankStar = Glyph(IconKind.Sparkle, IconVariant.Filled, Rgb(210, 180, 120), 10);
            rankStar.Margin = new Thickness(8, 0, 4, 0);
            pValue.Children.Add(rankStar);
            pValue.Children.Add(Text(Loc.GetF("label_prestige_rank", 1 + (int)(spent / 100)), Rgb(210, 180, 120), 10));
            foreach (var c in pValue.Children) c.VerticalAlignment = VerticalAlignment.Center;
            pInfo.Children.Add(pValue);
            prestige.Children.Add(pInfo);
            var prestigeBox = Box(Rgb(52, 44, 28), new Thickness(15, 6, 15, 6), prestige);
            ToolTip.SetTip(prestigeBox, Loc.Get("tooltip_prestige"));
            main.Children.Add(prestigeBox);
            _prestigeRow = prestigeBox;   // WPF _prestigeRowBorder, the prestige burst's anchor

            if (SkillTreeRules.HasSkill(settings, "ditzy_data"))
                AddDitzyStats(main, mods?.GetStatsTitle());

            AddProSections(main);

            // Stats: XP multiplier (+ rush), conditioning time.
            var xp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            xp.Children.Add(Text(Loc.Get("label_xp_mult"), Rgb(176, 176, 176), 12));
            xp.Children.Add(Text($"{SkillTreeRules.GetTotalXpMultiplier(settings, DateTime.Now.Hour, 0.0):F2}x", Rgb(0, 255, 136), 14, bold: true));
            if (settings.PinkRushActive)
            {
                var rush = IconLabel.ForKey("label_xp_rush");
                rush.Margin = new Thickness(4, 0, 0, 0);
                rush.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.Parse(mods?.GetAccentDarkColorHex() ?? "#FF1493")));
                rush.SetValue(TextElement.FontSizeProperty, 12.0);
                rush.SetValue(TextElement.FontWeightProperty, FontWeight.Bold);
                xp.Children.Add(rush);
            }
            foreach (var c in xp.Children) c.VerticalAlignment = VerticalAlignment.Center;
            var time = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var timer = Glyph(IconKind.Timer, IconVariant.Regular, Rgb(176, 176, 176), 12);
            timer.Margin = new Thickness(0, 0, 4, 0);
            time.Children.Add(timer);
            time.Children.Add(Text(SkillTreeRules.FormatConditioningTime(settings), Rgb(176, 176, 176), 12));
            var stats = new StackPanel();
            stats.Children.Add(xp);
            stats.Children.Add(time);
            main.Children.Add(new Border
            {
                Background = this.TryFindResource("SurfaceBgBrush", out var bg) && bg is IBrush b ? b : Rgb(30, 30, 58),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = stats,
            });

            _headerBrush = TreeBrush(header: true);
            return new Border
            {
                Width = 500,
                Background = _headerBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(15, 8, 15, 15),
                // The column outgrows the fixed-height canvas, so it scrolls its own content; the
                // wheel redirect above yields to it.
                Child = new ScrollViewer
                {
                    Content = main,
                    VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    MaxHeight = 430,
                },
            };
        }

        /// <summary>The Ditzy Data toggle and its collapsed 3-column stats grid.</summary>
        private static void AddDitzyStats(StackPanel main, string? statsTitle)
        {
            var arrow = Glyph(IconKind.ChevronDown, IconVariant.Regular, Rgb(176, 176, 176), 10);
            arrow.Margin = new Thickness(4, 0, 0, 0);
            var label = IconRow(Glyph(IconKind.DataBarVertical, IconVariant.Regular, null, 12),
                Text(statsTitle ?? Loc.Get("label_ditzy_data_stats"), AccentLight, 11, bold: true), arrow);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            // A focusable Border, not a Button: WPF's is a Border with MouseLeftButtonDown, and a
            // stock Button would draw Fluent chrome. Enter/Space reach it from the keyboard (P17).
            var toggle = new Border
            {
                Name = "DitzyStatsToggle",
                Background = Rgb(60, 40, 80),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                BorderBrush = Accent,
                BorderThickness = new Thickness(1),
                Focusable = true,
                Child = label,
            };

            var stack = new StackPanel();
            var statsHead = IconRow(Glyph(IconKind.DataBarVertical, IconVariant.Regular, Rgb(176, 176, 176), 11),
                Text(statsTitle ?? "Ditzy Data Stats", Rgb(176, 176, 176), 11, bold: true));
            statsHead.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(statsHead);
            if (App.Achievements?.Progress is { } a)
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
                int row = 0;
                void Row(params (string Key, string Value)[] cells)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    for (int col = 0; col < cells.Length; col++)
                    {
                        var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                        cell.Children.Add(Text(Loc.Get(cells[col].Key), Rgb(140, 140, 140), 9));
                        cell.Children.Add(Text(cells[col].Value, Brushes.White, 10, bold: true));
                        Grid.SetColumn(cell, col);
                        Grid.SetRow(cell, row);
                        grid.Children.Add(cell);
                    }
                    row++;
                }
                string Mins(double m) => m >= 60 ? $"{m / 60:F1} {Loc.Get("label_hrs")}" : $"{m:F1} {Loc.Get("label_min_abbrev")}";
                Row(("label_sessions_started", a.TotalSessionsStarted.ToString("N0")),
                    ("label_sessions_completed", a.CompletedSessions.Count.ToString("N0")),
                    ("label_sessions_abandoned", a.TotalSessionsAbandoned.ToString("N0")));
                Row(("label_total_xp_earned_stat", a.TotalXPEarned.ToString("N0")),
                    ("label_skill_points_earned", a.TotalSkillPointsEarned.ToString("N0")),
                    ("label_longest_session", $"{a.LongestSessionMinutes:F1} {Loc.Get("label_min_abbrev")}"));
                Row(("label_attention_passes", a.TotalAttentionChecksPassed.ToString("N0")),
                    ("label_video_att_passed", a.VideoAttentionChecksPassed.ToString("N0")),
                    ("label_video_att_failed", a.VideoAttentionChecksFailed.ToString("N0")));
                Row(("label_bubble_count_games", a.TotalBubbleCountGames.ToString("N0")),
                    ("label_bc_correct", a.TotalBubbleCountCorrect.ToString("N0")),
                    ("label_bc_best_streak", a.BubbleCountBestStreak.ToString("N0")));
                Row(("label_total_flashes_stat", a.TotalFlashImages.ToString("N0")),
                    ("label_bubbles_popped_stat", a.TotalBubblesPopped.ToString("N0")),
                    ("label_lock_cards_done", a.TotalLockCardsCompleted.ToString("N0")));
                Row(("label_video_time", Mins(a.TotalVideoMinutes)),
                    ("label_pink_filter_time", Mins(a.TotalPinkFilterMinutes)),
                    ("label_spiral_time", Mins(a.TotalSpiralMinutes)));
                Row(("label_consecutive_days", a.ConsecutiveDays.ToString("N0")));
                stack.Children.Add(grid);
            }
            var details = new Border
            {
                Name = "DitzyStatsPanel",
                Background = Rgb(22, 22, 42),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 15),
                IsVisible = false,
                Child = stack,
            };

            void Toggle()
            {
                details.IsVisible = !details.IsVisible;
                arrow.Kind = details.IsVisible ? IconKind.ChevronUp : IconKind.ChevronDown;
            }
            toggle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(toggle).Properties.IsLeftButtonPressed) Toggle(); };
            toggle.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Toggle(); e.Handled = true; } };
            main.Children.Add(toggle);
            main.Children.Add(details);
        }

        /// <summary>CreateSkillNode: art (blurred while locked; tier gradient when there is none),
        /// name, the three-state cost badge, the owned breath / purchasable glow, hover pop and the
        /// flavour + description + prerequisite tooltip.</summary>
        internal Control Node(SkillDefinition skill)
        {
            var settings = CoreSettings.Current;
            bool owned = SkillTreeRules.HasSkill(settings, skill.Id);
            bool canPurchase = SkillTreeRules.CanPurchaseSkill(settings, skill.Id);
            bool locked = !owned && !canPurchase;

            var grid = new Grid { RowDefinitions = new RowDefinitions("86,20,3,28") };
            Control art;
            if (!_art.TryGetValue(skill.Id, out var bitmap)) _art[skill.Id] = bitmap = ModArt.TryLoad($"skills/{skill.Id}.png", 312);
            art = bitmap != null
                ? new Image { Source = bitmap, Stretch = Stretch.UniformToFill }
                : new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), Background = Placeholder(skill.Tier) };
            // WPF blurs locked art with a BlurEffect (radius 8). An Effect re-blurs on every frame the tree's drifting
            // gradient repaints (P82 rule 3), so the blur is baked into the bitmap once per skill instead.
            if (locked && art is Image img && bitmap != null) img.Source = BlurredArt(skill.Id, bitmap) ?? bitmap;
            grid.Children.Add(art);

            var name = new Border
            {
                Background = Rgb(30, 28, 45),
                Child = new TextBlock
                {
                    Text = CoreMods.MakeModAware(skill.LocalizedName),
                    Foreground = Rgb(200, 200, 210),
                    FontSize = 9.5,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetRow(name, 1);
            grid.Children.Add(name);

            var cost = new Border
            {
                Background = owned ? Rgb(255, 200, 80) : canPurchase ? Accent : Rgb(40, 35, 50),
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                // WPF "💎N ★ FOREVER" / "💎 N" / "🔒 N": icons take the badge ink (gem violet or gold
                // on the gold/pink badge would vanish).
                Child = CostBadge(skill.Cost, owned ? IconKind.Diamond : canPurchase ? IconKind.Diamond : IconKind.LockClosed,
                    owned ? Rgb(20, 20, 30) : canPurchase ? Brushes.White : Rgb(120, 120, 130), owned),
            };
            Grid.SetRow(cost, 3);
            grid.Children.Add(cost);

            // The glow sits on an outer wrapper: an effect on the clipping Border itself would be
            // clipped away with the corners.
            var node = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                BorderThickness = new Thickness(owned ? 2 : 1),
                BorderBrush = owned ? Rgb(100, 255, 150) : canPurchase ? Accent : Rgb(60, 50, 70),
                Child = grid,
            };
            var wrapper = new Panel { Tag = skill.Id, RenderTransformOrigin = RelativePoint.Center, RenderTransform = TransformOperations.Parse("scale(1)") };
            // The owned / purchasable glow is a sibling BoxShadow layer behind the node, not a DropShadowEffect on
            // the wrapper (P82 rule 3: an Effect makes the node an offscreen layer re-blurred every frame); the
            // breath moves only its Opacity.
            if (owned || canPurchase)
            {
                var glow = new Border
                {
                    Width = NodeWidth, Height = NodeHeight, CornerRadius = new CornerRadius(10), IsHitTestVisible = false,
                    BoxShadow = new BoxShadows(new BoxShadow { Blur = owned ? 18 : 15, Color = owned ? Colors.LimeGreen : Colors.HotPink }),
                    Opacity = owned ? 0.6 : 0.7,
                };
                wrapper.Children.Add(glow);
                if (owned) _ownedGlows.Add(glow);
            }
            wrapper.Children.Add(node);

            wrapper.PointerEntered += (_, _) => ApplyNodeHover(wrapper, true);
            wrapper.PointerExited += (_, _) => ApplyNodeHover(wrapper, false);
            if (canPurchase) MakePurchasable(wrapper, skill.Id);

            var tip = new StackPanel { MaxWidth = 280 };
            tip.Children.Add(new TextBlock { Text = CoreMods.MakeModAware(skill.LocalizedFlavorText), Foreground = AccentLight, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            tip.Children.Add(new TextBlock { Text = CoreMods.MakeModAware(skill.LocalizedDescription), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !SkillTreeRules.HasSkill(settings, skill.PrerequisiteId))
            {
                var pre = SkillDefinition.All.FirstOrDefault(s => s.Id == skill.PrerequisiteId);
                var requires = IconLabel.ForKey("label_skill_requires", pre?.LocalizedName ?? skill.PrerequisiteId);
                requires.SetValue(TextElement.ForegroundProperty, Rgb(255, 100, 100));
                requires.Margin = new Thickness(0, 6, 0, 0);
                tip.Children.Add(requires);
            }
            ToolTip.SetTip(wrapper, Tip(tip, Rgb(30, 30, 50), Accent));
            return wrapper;
        }

        private static Border Tip(Control content, IBrush bg, IBrush border) => new()
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = content,
        };

        /// <summary>CreateSkillPlaceholderGradient, verbatim per tier.</summary>
        private static IBrush Placeholder(int tier)
        {
            var (a, b) = tier switch
            {
                1 => (Color.FromRgb(80, 50, 100), Color.FromRgb(50, 30, 70)),
                2 => (Color.FromRgb(100, 50, 80), Color.FromRgb(60, 30, 50)),
                3 => (Color.FromRgb(80, 60, 100), Color.FromRgb(45, 35, 65)),
                4 => (Color.FromRgb(100, 40, 90), Color.FromRgb(55, 25, 50)),
                6 => (Color.FromRgb(110, 85, 40), Color.FromRgb(60, 45, 25)),
                _ => (Color.FromRgb(60, 40, 80), Color.FromRgb(35, 25, 50)),
            };
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
            };
        }

        /// <summary>RefreshActiveBonuses: the hidden ActiveBonusesPanel/List pair WPF keeps in step.</summary>
        private void RefreshActiveBonuses()
        {
            var breakdown = SkillTreeRules.GetMultiplierBreakdown(CoreSettings.Current, DateTime.Now.Hour, 0.0);
            ActiveBonusesPanel.IsVisible = breakdown.Count > 1;
            ActiveBonusesList.Children.Clear();
            foreach (var (source, value) in breakdown.Skip(1))
                ActiveBonusesList.Children.Add(Chip($"{source}: +{value:P0}", new Thickness(10, 5, 10, 5), new Thickness(0, 0, 8, 8)));
        }

        private static Border Chip(string text, Thickness padding, Thickness margin) => new()
        {
            Background = Rgb(60, 40, 80),
            CornerRadius = new CornerRadius(12),
            Padding = padding,
            Margin = margin,
            Child = Text(text, AccentLight, 11),
        };

        private static Border Box(IBrush bg, Thickness padding, Control child) => new()
        {
            Background = bg,
            CornerRadius = new CornerRadius(12),
            Padding = padding,
            Margin = new Thickness(0, 0, 0, 15),
            Child = child,
        };

        /// <summary>PopulateSecretSkills: the requirement hint until IsSecretSkillAvailable, then
        /// the real card (owned / purchasable / revealed).</summary>
        private void PaintSecretRail()
        {
            var settings = CoreSettings.Current;
            foreach (var skill in SkillDefinition.All.Where(s => s.IsSecret))
                SecretSkills.Children.Add(SkillTreeRules.HasSkill(settings, skill.Id) || SkillTreeRules.IsSecretSkillAvailable(settings, skill.Id)
                    ? SecretSkillCard(skill)
                    : HiddenSecretCard(skill.LocalizedSecretRequirementDesc));
        }

        private static Control HiddenSecretCard(string requirement)
        {
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var padlock = Glyph(IconKind.LockClosed, IconVariant.Filled, Rgb(153, 50, 204), 18);
            padlock.Margin = new Thickness(0, 0, 8, 0);
            body.Children.Add(padlock);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            text.Children.Add(Text(Loc.Get("label_secret_skill_hidden"), Rgb(153, 50, 204), 11, bold: true));
            var hint = Text(requirement, Rgb(128, 128, 128), 8, top: 1);
            hint.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(hint);
            body.Children.Add(text);
            return SecretBorder(Rgb(30, 20, 40), Rgb(80, 60, 100), 1, body, opacity: 0.6);
        }

        /// <summary>CreateSecretSkillCard.</summary>
        internal Control SecretSkillCard(SkillDefinition skill)
        {
            var settings = CoreSettings.Current;
            bool owned = SkillTreeRules.HasSkill(settings, skill.Id);
            bool canPurchase = SkillTreeRules.CanPurchaseSkill(settings, skill.Id);

            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            if (!_art.TryGetValue(skill.Id, out var bitmap)) _art[skill.Id] = bitmap = ModArt.TryLoad($"skills/{skill.Id}.png", 312);
            // 44x30 is the 3:2 the skills/ art is drawn at. Without art, the skill's icon by id (IconMap.ForSkill).
            Control icon = bitmap != null
                ? new Image { Source = bitmap, Width = 44, Height = 30, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }
                : IconGlyph.Of(IconMap.ForSkill(skill.Id), skill.Icon, 18);
            icon.Margin = new Thickness(0, 0, 8, 0);
            body.Children.Add(icon);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(stack, 1);
            var name = Text(CoreMods.MakeModAware(skill.LocalizedName), owned ? Rgb(180, 130, 255) : Rgb(153, 50, 204), 11, bold: true);
            name.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(name);
            // WPF "💎N ✓ OWNED" / "💎 N".
            var price = owned
                ? IconRow(Glyph(IconKind.Diamond, IconVariant.Filled, Rgb(180, 130, 255), 9), Text($"{skill.Cost}", Rgb(180, 130, 255), 9),
                      Glyph(IconKind.Checkmark, IconVariant.Regular, Rgb(180, 130, 255), 9), Text(IconText.BareLoc("label_skill_owned"), Rgb(180, 130, 255), 9))
                : IconRow(Glyph(IconKind.Diamond, IconVariant.Filled, settings.SkillPoints >= skill.Cost ? Rgb(255, 215, 0) : Rgb(120, 120, 120), 10),
                      Text($"{skill.Cost}", settings.SkillPoints >= skill.Cost ? Rgb(255, 215, 0) : Rgb(120, 120, 120), 10));
            price.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(price);
            body.Children.Add(stack);

            var card = SecretBorder(owned ? Rgb(40, 30, 50) : canPurchase ? Rgb(50, 30, 60) : Rgb(35, 25, 45),
                owned ? Rgb(180, 100, 255) : canPurchase ? Rgb(153, 50, 204) : Rgb(100, 70, 130), owned ? 2 : 1, body, opacity: 1);
            card.Tag = skill.Id;
            // The card is a Border, so its glow is its own BoxShadow (WPF DropShadowEffect; P82 rule 3).
            if (owned) card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(128, Colors.Purple.R, Colors.Purple.G, Colors.Purple.B) });
            else if (canPurchase) card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Color = Color.FromArgb(102, Colors.MediumPurple.R, Colors.MediumPurple.G, Colors.MediumPurple.B) });

            var tip = new StackPanel { MaxWidth = 280 };
            tip.Children.Add(new TextBlock { Text = CoreMods.MakeModAware(skill.LocalizedFlavorText), Foreground = Rgb(200, 150, 255), FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            tip.Children.Add(new TextBlock { Text = CoreMods.MakeModAware(skill.LocalizedDescription), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            ToolTip.SetTip(card, Tip(tip, Rgb(40, 25, 55), Rgb(153, 50, 204)));
            if (canPurchase) MakePurchasable(card, skill.Id);
            return card;
        }

        private static Border SecretBorder(IBrush bg, IBrush border, double thickness, Control body, double opacity) => new()
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(thickness),
            CornerRadius = new CornerRadius(8),
            Width = SecretCardWidth,
            Height = SecretCardHeight,
            Margin = new Thickness(0, 3, 10, 3),
            Padding = new Thickness(8, 6, 8, 6),
            Opacity = opacity,
            Child = body,
        };

        /// <summary>Icons and texts on one centred line (WPF wrote the emoji into the string).</summary>
        private static StackPanel IconRow(params Control[] parts)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var c in parts) { c.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(c); }
            return row;
        }

        /// <summary>An icon in a fixed ink, or (brushKey) a live IconBrushes resource.</summary>
        private static IconGlyph Glyph(IconKind kind, IconVariant variant, IBrush? ink, double size, string? brushKey = null)
        {
            var g = IconGlyph.Of(kind, variant, brushKey, size);
            if (ink != null) g.Foreground = ink;
            return g;
        }

        private static StackPanel CostBadge(int cost, IconKind kind, IBrush ink, bool forever)
        {
            var parts = new List<Control> { Glyph(kind, IconVariant.Filled, ink, 10), Text($"{cost}", ink, 10, bold: true) };
            if (forever)
            {
                parts.Add(Glyph(IconKind.Star, IconVariant.Filled, ink, 10));
                parts.Add(Text(IconText.BareLoc("label_skill_permanent"), ink, 10, bold: true));
            }
            var badge = IconRow(parts.ToArray());
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.VerticalAlignment = VerticalAlignment.Center;
            return badge;
        }

        private static TextBlock Text(string text, IBrush colour, double size,
                                      bool bold = false, bool italic = false, double top = 0, double bottom = 0) => new()
        {
            Text = text,
            Foreground = colour,
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Margin = new Thickness(0, top, 0, bottom),
        };
    }
}
