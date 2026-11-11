// PORTED from ConditioningControlPanel/Controls/Billboard/BillboardCardHost.cs + .Chips.cs (the
// Tonight Board deck on Home): the card face (poster art, shade, eyebrow, title, line, one button,
// badge), the snooze x with its toast, the "paused" pill, the dot row whose fill IS the 12 s hold.
// It plays no sound (WPF c0c67ff47 silenced every board cue). The juice (push, glint, text rise,
// thud, badge pop, fold, dot open/tick/pop, sparks, motes) lives in BillboardCardHost.Fx.cs, the CTA
// glow, shine and press motion in BillboardCardHost.Cta.cs. Not yet here: the drawn art views (board
// tiles, wheel, invite, tip and house scenes, living posters); house cards wear their poster PNG.

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.UI;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF BillboardCardHost: one card at a time from a <see cref="BillboardDeck"/>, a row
    /// of dots under it with the hold filling the current one.</summary>
    internal sealed partial class BillboardCardHost : Grid
    {
        /// <summary>The hold reads elapsed time here; tests step it (P08/P40).</summary>
        internal static TimeProvider Time = TimeProvider.System;

        private static readonly Color Plate = Color.FromRgb(0x12, 0x13, 0x27);
        private static readonly Color Ink = Color.FromRgb(0x0d, 0x0e, 0x1c);
        // WPF BillboardCardHost.cs:33 Display = Fredoka (title, button), :34 Mono (eyebrow, badge, pill).
        private static readonly FontFamily Display = new("Fredoka, Segoe UI");

        private readonly BillboardDeck _deck;
        private readonly Border _stage = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true };
        private readonly Panel _slides = new();
        private readonly StackPanel _meta = new() { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _eyebrow = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly TextBlock _title = new() { FontWeight = FontWeight.Bold, FontSize = 30, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly TextBlock _line = new() { FontSize = 14, Margin = new Thickness(0, 4, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xc9, 0xc7, 0xee)), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
        private readonly Button _cta;
        private readonly Border _ctaFace = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        // WPF :448-450 the raised drop under the plank, in the card hue's shadow (depth law).
        private readonly Border _ctaDrop = new() { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, DepthLaw.RaisedPx, 0, -DepthLaw.RaisedPx), IsHitTestVisible = false };
        // WPF :463 the keyboard focus ring.
        private readonly Border _ctaRing = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(-3), IsHitTestVisible = false, IsVisible = false };
        private readonly TextBlock _ctaText = new() { FontWeight = FontWeight.Bold, FontSize = 14, Margin = new Thickness(18, 7, 18, 8), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Border _badge = new() { Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, IsVisible = false };
        private readonly TextBlock _badgeText = new() { FontSize = 11, FontWeight = FontWeight.Bold };
        private readonly Button _snooze;
        private readonly Border _paused = new();
        private readonly Border _toast = new() { RenderTransform = new TranslateTransform() };
        private readonly TextBlock _toastText = new() { FontSize = 13 };
        private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        private Control? _current;
        private DeckCard? _card;
        private bool _pointerOver, _folding, _begun;
        private Rectangle? _fill;
        private double _holdBase;
        private long _holdSince;
        private Helpers.BeatLoop? _holdTimer;
        private IDisposable? _visibilityWatch;

        /// <summary>The button (or a click on the card) asks for this card's action.</summary>
        public event Action<DeckCard>? ActionRequested;

        internal DeckCard? CurrentCard => _card;
        internal BillboardDeck Deck => _deck;
        internal global::Avalonia.Controls.Controls ChipButtons => _chips.Children;
        internal Button Cta => _cta;
        internal Button SnoozeButton => _snooze;
        internal string ToastText => _toastText.Text ?? string.Empty;
        internal bool HoldRunning => _holdTimer != null;
        internal double HoldProgress => Math.Clamp(_holdBase + (HoldRunning ? Elapsed() : 0), 0, 1);

        public BillboardCardHost(BillboardDeck deck)
        {
            _deck = deck ?? throw new ArgumentNullException(nameof(deck));
            RowDefinitions = new RowDefinitions("*,Auto");

            // WPF :83-129, in its z-order: slides, glint, words, badge, snooze, pill, toast, sparks.
            var stageRoot = _stageRoot;
            stageRoot.Children.Add(_slides);
            _glint.RenderTransform = _glintMove;
            stageRoot.Children.Add(_glint);
            _eyebrow.FontFamily = MonoFont;
            _title.FontFamily = Display;
            _title.Foreground = (IBrush?)FindRes("TextLightBrush") ?? Brushes.White;
            foreach (var t in new[] { _eyebrow, _title, _line }) t.RenderTransform = new TranslateTransform();
            _cta = BuildCta();
            _meta.Children.Add(_eyebrow);
            _meta.Children.Add(_title);
            _meta.Children.Add(_line);
            _meta.Children.Add(_cta);
            _meta.RenderTransform = _metaMove;
            stageRoot.Children.Add(_meta);
            _badgeText.FontFamily = MonoFont;
            _badge.Child = _badgeText;
            // WPF :505-506 the badge pops (scale) and folds (move) about its centre.
            _badge.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            _badge.RenderTransform = new TransformGroup { Children = { _badgeScale, _badgeMove } };
            stageRoot.Children.Add(_badge);
            _snooze = BuildSnooze();
            stageRoot.Children.Add(_snooze);
            stageRoot.Children.Add(BuildPill(_paused, new TextBlock { Text = Loc.Get("billboard_deck_paused"), FontFamily = MonoFont, FontSize = 11, Foreground = MutedBrush }, top: true));
            stageRoot.Children.Add(BuildPill(_toast, _toastText, top: false));
            _toastText.Foreground = (IBrush?)FindRes("TextLightBrush") ?? Brushes.White;
            stageRoot.Children.Add(_sparks);
            _stage.Child = stageRoot;
            Children.Add(_stage);

            var chipRow = new Border { Padding = new Thickness(0, 8, 0, 2), Child = _chips };
            SetRow(chipRow, 1);
            Children.Add(chipRow);

            _stage.SizeChanged += (_, _) => Lay();
            _stage.PointerEntered += (_, _) => { _pointerOver = true; OnHoverChanged(); };
            _stage.PointerExited += (_, _) => { _pointerOver = false; OnHoverChanged(); };
            _stage.PointerPressed += OnStagePressed;
            PropertyChanged += (_, e) => { if (e.Property == IsKeyboardFocusWithinProperty) UpdateRunning(); };
        }

        private static object? FindRes(string key) =>
            Application.Current?.TryFindResource(key, out var v) == true ? v : null;

        private static IBrush MutedBrush => (IBrush?)FindRes("TextMutedBrush") ?? Brushes.Gray;

        private static FontFamily MonoFont => (FontFamily?)FindRes("Font.Mono") ?? FontFamily.Default;

        /// <summary>A Theme/Depth.xaml brush (WPF SetResourceReference on the same key).</summary>
        private static IBrush? Depth(string key) => FindRes(key) as IBrush;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _visibilityWatch = EffectiveVisibility.Watch(this, () => { UpdateRunning(); ApplyDirty(); });
            UpdateRunning();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _visibilityWatch?.Dispose();
            _visibilityWatch = null;
            (_current?.Tag as IBillboardArtView)?.Hold();
            _ctaOnScreen = false;
            StopCtaFx();
            PauseHold();
            _sparks.Clear();
            base.OnDetachedFromVisualTree(e);
        }

        // ---- the walk (WPF :149-258) -------------------------------------------------------------

        /// <summary>Builds the deck and shows its first card, still.</summary>
        public void Begin()
        {
            _begun = true;
            Show(_deck.Start(), animate: false);
        }

        /// <summary>The next card (the hold ran out, or someone asked).</summary>
        public void Advance()
        {
            if (!_begun) { Begin(); return; }
            if (_folding) return;
            Show(_deck.Next(), animate: true);
        }

        public void MarkDirty()
        {
            _deck.MarkDirty();
            ApplyDirty();
        }

        private void ApplyDirty()
        {
            if (!_begun || _folding || !IsEffectivelyVisible ||
                TopLevel.GetTopLevel(this) is not { IsVisible: true } || _card == null || !_deck.IsDirty) return;
            double progress = HoldProgress;
            string ids = DeckIds();
            bool arrived = _deck.Refresh();
            if (_deck.IsDirty) return;
            if (arrived && DashboardBillboard.JumpToArrival(progress, HoldMayRun)) { Advance(); return; }
            if (ids == DeckIds()) return;
            RebuildChips(tick: false);
            _holdBase = progress;
            SetFill(progress);
            UpdateRunning();
        }

        private string DeckIds() => string.Join("\n", _deck.Cards.Select(c => c.Spec.Id));

        /// <summary>The shell closed: every art view frees its player and bitmaps (P68).</summary>
        public void ReleaseArt()
        {
            _ctaOnScreen = false;
            StopCtaFx();
            foreach (var slide in _slides.Children) (slide.Tag as IBillboardArtView)?.Release();
        }

        /// <summary>The motion level or a visibility gate changed: re-read every gate now.</summary>
        public void RefreshMotion() { UpdateRunning(); ApplyDirty(); }

        internal bool HoldMayRun =>
            !_folding && _card != null && _fill != null && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is { IsVisible: true } &&
            DashboardBillboard.ShouldAdvance(_pointerOver, onScreen: true, Env.Level != MotionLevel.Off, IsKeyboardFocusWithin);

        private void Show(DeckCard? card, bool animate)
        {
            if (card == null) { ClearStage(); return; }
            bool same = _card != null && _current != null && string.Equals(_card.Spec.Id, card.Spec.Id, StringComparison.Ordinal);
            _card = card;
            animate &= Env.AllowTransitions && !same;
            if (!same)
            {
                var slide = BuildSlide(card);
                var old = _current;
                _current = slide;
                _slides.Children.Add(slide);
                if (old != null)
                {
                    (old.Tag as IBillboardArtView)?.Hold();
                    if (animate) Push(old, slide);
                    else Retire(old);
                }
            }
            PaintWords(card);
            PaintBadge(card);
            _snooze.IsVisible = card.CanSnooze;
            RebuildChips();
            Lay();
            // WPF :233-237: the words rise (or settle), then motes drift out of them.
            if (animate) LandWords();
            else SettleWords();
            LandCtaFx(animate);
            if (!same && animate && !DashboardBillboard.IsFullBleed(card.Spec))
                After(DashboardBillboard.MotesDelayMs, () => { if (ReferenceEquals(_card, card)) Motes(card); });
            UpdateRunning();
        }

        private void ClearStage()
        {
            foreach (var slide in _slides.Children.ToArray()) Retire(slide);
            _current = null;
            _card = null;
            _ctaOnScreen = false;
            StopCtaFx();
            _meta.IsVisible = _badge.IsVisible = _snooze.IsVisible = false;
            _sparks.Clear();
            _chips.Children.Clear();
            _fill = null;
            PauseHold();
        }

        /// <summary>WPF Retire: the slide's art lets go of its player, then the slide leaves.</summary>
        private void Retire(Control slide)
        {
            if (_slideRuns.Remove(slide, out var run)) { run.Cancel(); run.Dispose(); }
            try { (slide.Tag as IBillboardArtView)?.Release(); } catch (Exception ex) { Log.Debug("Billboard art release failed: {E}", ex.Message); }
            _slides.Children.Remove(slide);
        }

        /// <summary>WPF UpdateRunning: another tab or a fold holds the art; hover never does.</summary>
        private void UpdateRunning()
        {
            var art = _current?.Tag as IBillboardArtView;
            bool onScreen = IsEffectivelyVisible && TopLevel.GetTopLevel(this) is { IsVisible: true };
            try
            {
                if (DashboardBillboard.ArtShouldPlay(onScreen, _folding)) art?.Run();
                else art?.Hold();
            }
            catch (Exception ex) { Log.Debug("Billboard art run/hold failed: {E}", ex.Message); }
            RefreshCtaFx(onScreen && !_folding && _card != null && _cta.IsVisible);
            if (HoldMayRun) ResumeHold();
            else PauseHold();
        }

        /// <summary>WPF :283-288: the pill and the snooze x fade in 200 ms (at once when Motion is Off).</summary>
        private void OnHoverChanged()
        {
            FadeTo(_paused, _pointerOver && Env.Level != MotionLevel.Off ? 1 : 0, 200);
            FadeTo(_snooze, _pointerOver ? 1 : 0, 200);
            UpdateRunning();
        }

        // ---- words (WPF :267-335) ----------------------------------------------------------------

        private void PaintWords(DeckCard card)
        {
            var spec = card.Spec;
            var hue = HueOf(card);
            bool bleed = DashboardBillboard.IsFullBleed(spec);
            _meta.IsVisible = true;
            _eyebrow.Text = (spec.Eyebrow ?? string.Empty).ToLowerInvariant();
            _eyebrow.Foreground = new SolidColorBrush(hue);
            _title.Text = spec.Title ?? string.Empty;
            _line.Text = spec.Line ?? string.Empty;
            _eyebrow.IsVisible = !bleed && _eyebrow.Text.Length > 0;
            _title.IsVisible = !bleed && _title.Text.Length > 0;
            _line.IsVisible = !bleed && _line.Text.Length > 0;

            var action = card.Action;
            bool hasButton = action.Kind != BillboardActionKind.None;
            _cta.IsVisible = hasButton;
            _ctaText.Text = action.Label;
            _ctaFace.Background = new SolidColorBrush(hue);
            _ctaDrop.Background = new SolidColorBrush(ShadowOf(hue));
            _cta.Margin = new Thickness(0, bleed ? 0 : 12, 0, 0);
            AutomationProperties.SetName(_cta, string.IsNullOrEmpty(spec.Title) ? action.Label : action.Label + ", " + spec.Title);
            _stage.Cursor = !bleed && hasButton ? new Cursor(StandardCursorType.Hand) : null;
            _meta.HorizontalAlignment = bleed ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void PaintBadge(DeckCard card)
        {
            var key = DashboardBillboard.BadgeKey(card.Badge);
            if (key == null) { _badge.IsVisible = false; return; }
            var hue = card.Badge switch
            {
                BillboardBadge.New => Color.FromRgb(0xff, 0x4f, 0xa8),
                BillboardBadge.Basic => Color.FromRgb(0xff, 0xc9, 0x4a),
                _ => Color.FromRgb(0x5f, 0xe3, 0xff),
            };
            _badgeText.Text = Loc.Get(key).ToUpperInvariant();
            _badgeText.Foreground = new SolidColorBrush(hue);
            _badge.BorderBrush = new SolidColorBrush(hue);
            _badge.Background = new SolidColorBrush(hue, 0.10);
            _badge.IsVisible = true;
        }

        private void Lay()
        {
            double w = _stage.Bounds.Width, h = _stage.Bounds.Height;
            if (w <= 0 || h <= 0) return;
            bool bleed = _card != null && DashboardBillboard.IsFullBleed(_card.Spec);
            double side = Math.Clamp(w * 0.04, 14, 32), foot = Math.Clamp(h * 0.06, 14, 34);
            _meta.Margin = bleed
                ? new Thickness(0, 0, Math.Clamp(w * 0.02, 10, 18), Math.Clamp(h * 0.03, 10, 16))
                : new Thickness(side, 0, 0, foot);
            _meta.MaxWidth = Math.Max(160, w * 0.46);
            _title.FontSize = Math.Clamp(h * 0.09, 20, 34);
            _line.FontSize = Math.Clamp(h * 0.042, 12, 15);
            _badge.Margin = new Thickness(side, 14, 0, 0);
            _glint.Width = w * 0.07;
            _glint.Height = h;
        }

        // ---- input (WPF :337-412) ----------------------------------------------------------------

        private void OnStagePressed(object? sender, PointerPressedEventArgs e)
        {
            if (_card == null || _folding || !e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed) return;
            for (var v = e.Source as Visual; v != null && !ReferenceEquals(v, _stage); v = v.GetVisualParent())
                if (v is Button) return;
            if (DashboardBillboard.IsFullBleed(_card.Spec))
            {
                // WPF :366-380 a touch on the board: the art ripples from that tile (even hover-paused).
                // WPF's two ripple notes (LauncherSfx.BoardTouch) have no player on this head yet.
                if (_current?.Tag is BoardTileView board && board.Bounds.Width > 0 && board.Bounds.Height > 0)
                {
                    var p = e.GetPosition(board);
                    try { board.Touch(new Point(Math.Clamp(p.X / board.Bounds.Width, 0, 1), Math.Clamp(p.Y / board.Bounds.Height, 0, 1))); }
                    catch (Exception ex) { Log.Debug("Billboard touch failed: {E}", ex.Message); }
                }
                return;
            }
            // Anywhere else on a card with a button is the button.
            if (_card.Action.Kind != BillboardActionKind.None) Press(e.GetPosition(_stageRoot));
        }

        /// <summary>WPF Press (:397-408): sparks in the hue and the (silenced) chime, then the action
        /// PressActionDelayMs later so the sparks are seen before a tab switch.</summary>
        private void Press(Point at)
        {
            var card = _card;
            if (card == null) return;
            Burst(at, HueOf(card), 26, 1.0);
            After(DashboardBillboard.PressActionDelayMs, () =>
            {
                try { ActionRequested?.Invoke(card); }
                catch (Exception ex) { Log.Warning(ex, "Billboard action failed"); }
            });
        }

        /// <summary>WPF OnCtaClick (:410-415): the sparks fly from the button's centre.</summary>
        private void OnCtaClick()
        {
            if (_card == null || _folding) return;
            Press(CenterOf(_cta));
        }

        private Point CenterOf(Control c) =>
            c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), _stageRoot) ?? default;

        private void OnSnoozeClick()
        {
            var card = _card;
            if (card == null || _folding || !card.CanSnooze) return;
            // WPF OnSnoozeClick (:417-435): sparks off the x, the words fold, the dot pops, then the
            // next card lands and the toast names the one that left.
            _folding = true;
            UpdateRunning();
            Burst(CenterOf(_snooze), HueOf(card), 16, 0.7);
            Fold();
            PopCurrentChip();
            After(Env.AllowTransitions ? DashboardBillboard.SnoozeFoldMs : 0, () =>
            {
                _folding = false;
                var label = ChipLabel(card);
                Show(_deck.SnoozeCurrent(DateTime.UtcNow), animate: true);
                Toast(string.Format(CultureInfo.CurrentCulture, Loc.Get("billboard_deck_snoozed"), label));
            });
        }

        // ---- pieces (WPF :414-600) ---------------------------------------------------------------

        private static FuncControlTemplate<Button> Bare() => new((b, _) => new ContentPresenter
        {
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        });

        private Button BuildCta()
        {
            // WPF BuildCta (:439-490) + BuildCtaFx: glow, drop, bevelled face (sheen, shine, label), focus ring.
            _ctaText.FontFamily = Display;
            _ctaText.Foreground = new SolidColorBrush(Ink);
            var sheen = new Border { CornerRadius = new CornerRadius(9), IsHitTestVisible = false, Background = Depth("DepthRaisedSheen") };
            var face = new Grid { ClipToBounds = true, Children = { sheen, _ctaShine, _ctaText } };
            _ctaFace.Child = face;
            _ctaFace.BorderBrush = Depth("DepthRaisedBevel");
            _ctaRing.BorderBrush = (IBrush?)FindRes("TextLightBrush") ?? Brushes.White;
            var root = new Grid { Children = { _ctaGlow, _ctaDrop, _ctaFace, _ctaRing } };
            BuildCtaFx(root);
            var button = new Button
            {
                Template = Bare(), Content = root, Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left, Focusable = true, Margin = new Thickness(0, 12, 0, 0),
                // WPF :466 the landing thud (scale + drop) rides the button, about its centre.
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { _ctaThudScale, _ctaThudMove } },
            };
            button.PointerEntered += (_, _) => SetCtaHover(true);
            button.PointerExited += (_, _) => { SetCtaPressed(false); SetCtaHover(false); };
            button.AddHandler(PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) SetCtaPressed(true); }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, (_, _) => SetCtaPressed(false), global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.PointerCaptureLost += (_, _) => SetCtaPressed(false);
            button.Click += (_, _) => OnCtaClick();
            // WPF :486 the ring shows for keyboard focus only.
            button.GotFocus += (_, e) => _ctaRing.IsVisible = IsKeyboard(e.NavigationMethod);
            button.LostFocus += (_, _) => _ctaRing.IsVisible = false;
            return button;
        }

        private static bool IsKeyboard(NavigationMethod m) => m is NavigationMethod.Tab or NavigationMethod.Directional;

        private Button BuildSnooze()
        {
            // WPF BuildSnooze (:509-539): a raised coin - a drop disc under it, a bevelled face, the x.
            // The x is the head's Dismiss icon (this head draws icons, not glyph text).
            var drop = new Ellipse { Margin = new Thickness(1, DepthLaw.RaisedPx, -1, -DepthLaw.RaisedPx), IsHitTestVisible = false, Fill = Depth("DepthDropDisc") };
            var face = new Border
            {
                CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xc0, 12, 12, 26)), BorderBrush = Depth("DepthRaisedBevel"),
                Child = new IconGlyph { Kind = IconKind.Dismiss, Size = 14, Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var coin = new Grid { Width = 28, Height = 28, Children = { drop, face } };
            var button = new Button
            {
                Template = Bare(), Content = coin, Cursor = new Cursor(StandardCursorType.Hand), Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0),
            };
            ToolTip.SetTip(button, Loc.Get("billboard_deck_snooze"));
            AutomationProperties.SetName(button, Loc.Get("billboard_deck_snooze"));
            button.Click += (_, _) => OnSnoozeClick();
            button.PointerEntered += (_, _) => face.BorderBrush = new SolidColorBrush(Color.FromRgb(0xff, 0x4f, 0xa8));
            button.PointerExited += (_, _) => face.BorderBrush = Depth("DepthRaisedBevel");
            button.GotFocus += (_, e) => { if (IsKeyboard(e.NavigationMethod)) { StopFade(button); button.Opacity = 1; } };
            return button;
        }

        private static Border BuildPill(Border pill, TextBlock text, bool top)
        {
            pill.Child = text;
            pill.Padding = top ? new Thickness(10, 3, 10, 4) : new Thickness(14, 6, 14, 7);
            pill.CornerRadius = new CornerRadius(999);
            pill.BorderThickness = new Thickness(1);
            // WPF :549 DepthPressedBevel (the pill), :566 DepthFloatRim (the toast).
            pill.BorderBrush = Depth(top ? "DepthPressedBevel" : "DepthFloatRim");
            pill.Background = top ? new SolidColorBrush(Color.FromArgb(0xb3, 10, 10, 22)) : new SolidColorBrush(Color.FromRgb(0x24, 0x25, 0x4a));
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            pill.Margin = top ? new Thickness(0, 14, 0, 0) : new Thickness(0, 0, 0, 14);
            pill.IsHitTestVisible = false;
            pill.Opacity = 0;
            return pill;
        }

        /// <summary>WPF Slide: the registered art view, a poster, or a plain ground, and the shade.
        /// The slide's Tag carries the art's lifecycle.</summary>
        private static Control BuildSlide(DeckCard card)
        {
            // WPF Slide (:628-641): the push scales then moves the slide, about its centre.
            var root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0d, 0x1a)),
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
            };
            Control? art = null;
            try { art = BillboardArt.Create(card.Spec.ArtKey ?? string.Empty, card.Spec.ArtData); }
            catch (Exception ex) { Log.Debug("Billboard art {Key} did not build: {E}", card.Spec.ArtKey, ex.Message); }
            // WPF :645 a view the host can tint takes the card's hue.
            if (art is IAccentedArt tinted) tinted.Accent = HueOf(card);
            if (art != null)
            {
                root.Children.Add(art);
                root.Tag = art as IBillboardArtView;
            }
            else if (card.Spec.ArtKey == BuiltInArtKeys.Poster && card.Spec.ArtData is string poster)
            {
                try { root.Children.Add(new Image { Stretch = Stretch.UniformToFill, Source = new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + poster))) }); }
                catch (Exception ex) { Log.Debug("Billboard poster {Poster} did not load: {E}", poster, ex.Message); }
            }
            if (!DashboardBillboard.IsFullBleed(card.Spec))
            {
                root.Children.Add(new Border
                {
                    IsHitTestVisible = false,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromArgb(0xeb, 9, 9, 20), 0), new GradientStop(Color.FromArgb(0x99, 9, 9, 20), 0.38), new GradientStop(Color.FromArgb(0, 9, 9, 20), 0.62) },
                    },
                });
            }
            return root;
        }

        // ---- the dots and the hold (WPF .Chips.cs) -----------------------------------------------

        /// <summary>WPF .Chips.cs:63-73: a resting dot, the current capsule, hover growth, hit room.</summary>
        internal const double DotPx = 8, DotCurrentPx = 26, DotHoverScale = 1.3;
        private const double DotPadX = 5, DotPadY = 6;

        private Control? _currentChip;

        private void RebuildChips(bool tick = true)
        {
            PauseHold();
            _holdBase = 0;
            _chips.Children.Clear();
            _fill = null;
            _currentChip = null;
            var cards = _deck.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                bool current = i == _deck.Index;
                var chip = BuildChip(cards[i], current, out var fill, out var open);
                chip.Click += (_, _) => OnChipClick(index);
                _chips.Children.Add(chip);
                open?.Invoke(); // on the tree, so it runs on the host's clock
                if (current) { _fill = fill; _currentChip = chip; }
            }
            // WPF .Chips.cs:60 the current dot ticks as its card lands.
            if (tick && _currentChip != null && Env.AllowTransitions) Tick(_currentChip);
        }

        /// <summary>WPF BuildChip (.Chips.cs:75-211): a raised dot in its card's tint over a drop band;
        /// the current one sits pressed in, opens into a capsule and fills as the hold runs.</summary>
        private static Button BuildChip(DeckCard card, bool current, out Rectangle fill, out Action? open)
        {
            var tint = ChipTint(card) ?? HueOf(card);
            double w = current ? DotCurrentPx : DotPx;
            fill = new Rectangle
            {
                Fill = new SolidColorBrush(tint, 0xee / 255.0), HorizontalAlignment = HorizontalAlignment.Left,
                // The hold scales the fill (render only), never its Width (a layout pass per tick).
                Width = DotCurrentPx - 2, IsVisible = current, IsHitTestVisible = false,
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = new ScaleTransform(0, 1),
            };
            var sheen = new Border { IsHitTestVisible = false, Background = Depth(current ? "DepthPressedShade" : "DepthRaisedSheen") };
            var rest = Blend(Plate, tint, current ? 0.22 : 0.62);
            var hover = Blend(Plate, tint, 0.9);
            var faceFill = new SolidColorBrush(rest);
            var faceMove = new TranslateTransform(0, current ? DepthLaw.ActiveSinkPx : 0);
            IBrush? restBorder = current ? new SolidColorBrush(Blend(tint, Colors.White, 0.25)) : Depth("DepthRaisedBevel");
            var face = new Border
            {
                Width = w, Height = DotPx, CornerRadius = new CornerRadius(DotPx / 2),
                BorderThickness = new Thickness(1), ClipToBounds = true, Background = faceFill, BorderBrush = restBorder,
                Child = new Panel { Children = { fill, sheen } }, RenderTransform = faceMove,
            };
            // Depth law: a raised dot carries its drop band; the current one sits pressed in.
            var drop = new Border
            {
                Width = w, Height = DotPx, CornerRadius = new CornerRadius(DotPx / 2), IsHitTestVisible = false,
                Margin = new Thickness(0, DepthLaw.RaisedPx, 0, -DepthLaw.RaisedPx), Background = Depth("DepthDropBand"),
                Opacity = current ? 0 : 1,
            };
            var grow = new ScaleTransform(1, 1);
            var dot = new Panel
            {
                Margin = new Thickness(DotPadX, DotPadY, DotPadX, DotPadY),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = grow,
                Children = { drop, face },
            };
            // The root keeps the landing tick and the snooze pop; the dot inside takes the hover growth.
            var root = new Panel
            {
                Background = Brushes.Transparent,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = new ScaleTransform(1, 1),
                Children = { dot },
            };
            var name = ChipLabel(card);
            if (string.IsNullOrWhiteSpace(name)) name = card.Spec.Title ?? string.Empty;
            var button = new Button
            {
                Template = Bare(), Content = root, Cursor = new Cursor(StandardCursorType.Hand), Focusable = true,
                Margin = new Thickness(0, 0, 0, DepthLaw.RaisedPx),
            };
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
            CancellationTokenSource? hoverRun = null;
            void Hover(bool on)
            {
                double to = on ? DotHoverScale : 1, from = grow.ScaleX;
                hoverRun?.Cancel();
                hoverRun = new CancellationTokenSource();
                Easing ease = on ? new ThudEase() : new QuadraticEaseOut();
                int ms = Env.AllowTransitions ? (on ? 140 : 180) : 0;
                _ = Tween(dot, grow, ScaleTransform.ScaleXProperty, from, to, 0, ms, ease, hoverRun.Token);
                _ = Tween(dot, grow, ScaleTransform.ScaleYProperty, from, to, 0, ms, ease, hoverRun.Token);
                if (!current)
                {
                    faceFill.Color = on ? hover : rest;
                    // Depth law: hover lifts a raised dot.
                    faceMove.Y = on ? -DepthLaw.HoverLiftPx / 2 : 0;
                }
            }
            button.PointerEntered += (_, _) => Hover(true);
            button.PointerExited += (_, _) => Hover(false);
            button.GotFocus += (_, e) => { if (IsKeyboard(e.NavigationMethod)) face.BorderBrush = Brushes.White; };
            button.LostFocus += (_, _) => face.BorderBrush = restBorder;
            // Landing: the current dot opens from a dot into its capsule (260 ms thud).
            open = null;
            if (current && Env.AllowTransitions)
                open = () =>
                {
                    _ = Tween(face, face, WidthProperty, DotPx, DotCurrentPx, 0, 260, new ThudEase(), default);
                    _ = Tween(drop, drop, WidthProperty, DotPx, DotCurrentPx, 0, 260, new ThudEase(), default);
                };
            return button;
        }

        private void OnChipClick(int index)
        {
            if (_folding) return;
            if (index == _deck.Index) { PauseHold(); _holdBase = 0; SetFill(0); UpdateRunning(); return; }
            Show(_deck.Select(index), animate: true);
        }

        private double Elapsed() => Time.GetElapsedTime(_holdSince).TotalMilliseconds / (DashboardBillboard.HoldSeconds * 1000.0);

        private void SetFill(double progress)
        {
            if (_fill?.RenderTransform is ScaleTransform scale) scale.ScaleX = Math.Clamp(progress, 0, 1);
        }

        /// <summary>Runs the fill from where it stands; the fill reaching the end moves the deck on, so
        /// the bar and the change never disagree (WPF ResumeHold).</summary>
        private void ResumeHold()
        {
            if (_holdTimer != null) return;
            _holdSince = Time.GetTimestamp();
            // WPF BillboardCardHost.Chips.cs:278 runs the fill as a DoubleAnimation; here it rides the window's shared
            // 30 fps beat (it was a free-running 25 fps timer that ticked between the beat's frames: lag-fixes-b).
            _holdTimer = new Helpers.BeatLoop(this, _ => HoldTick());
            _holdTimer.Start();
        }

        /// <summary>One frame of the hold (the timer's handler; tests step <see cref="Time"/> and call it).</summary>
        internal void HoldTick()
        {
            if (_holdTimer == null) return;
            double p = HoldProgress;
            SetFill(p);
            if (p >= 1) { PauseHold(); Advance(); }
        }

        private void PauseHold()
        {
            if (_holdTimer == null) return;
            _holdBase = HoldProgress;
            _holdTimer.Stop();
            _holdTimer = null;
            SetFill(_holdBase);
        }

        // ---- helpers -----------------------------------------------------------------------------

        internal static Color HueOf(DeckCard card) =>
            Color.TryParse(DashboardBillboard.Accent(card.Spec.AccentHex), out var c) ? c : Color.FromRgb(0xff, 0x4f, 0xa8);

        internal static Color? ChipTint(DeckCard card) => card.Spec.Kind switch
        {
            BillboardCardKind.Board => Color.FromRgb(0xff, 0x4f, 0xa8),
            BillboardCardKind.Showcase when card.Spec.Badge == BillboardBadge.Prime => Color.FromRgb(0x5f, 0xe3, 0xff),
            BillboardCardKind.Showcase => Color.FromRgb(0xff, 0xc9, 0x4a),
            _ => null,
        };

        internal static string ChipLabel(DeckCard card)
        {
            var key = DashboardBillboard.ChipKey(card.Spec);
            return key != null ? Loc.Get(key) : card.Spec.Title ?? string.Empty;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
