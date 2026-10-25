using System;
using System.Linq;
using ConditioningControlPanel.Tutorial;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;

// The step model now comes from the tutorial seam. Aliased to the names the WPF original uses so
// this file still reads against ConditioningControlPanel/Windows/TutorialOverlay.xaml.cs line for
// line - and so the mapping (Models.TutorialStep -> CoreTutorial.Step) is stated once, here.
using TutorialStep = ConditioningControlPanel.CoreTutorial.Step;
using TutorialStepPosition = ConditioningControlPanel.CoreTutorial.StepPosition;
using TutorialAdvanceTrigger = ConditioningControlPanel.CoreTutorial.AdvanceTrigger;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The tutorial's full-screen coach mark: a dim sheet over the app with an optional lit hole
    /// around the control the current step is about, plus the card that carries the step's icon,
    /// title, description and its Skip / Skip step / Previous / Next row.
    ///
    /// Adapted from WPF's TutorialOverlay: card, spotlight and automatic advance subscriptions.
    /// Click-through input: the spotlight excludes its target rectangle from the native input
    /// region, and transparent waiting steps retain input only on their card (X11 uses XFixes;
    /// Windows uses SetWindowRgn, as WPF does).
    /// ponytail: the in-window stage, cross-window retarget and narration remain in follow-up
    /// wire-win-tutorial-overlay-b. The parameterless constructor renders a sample and never
    /// subscribes to the live tour.
    /// </summary>
    public partial class TutorialOverlay : Window
    {
        // ---- Named parts ------------------------------------------------------------

        private readonly Canvas _spotlightCanvas;
        private readonly Border _textPanel;
        private readonly TextBlock _txtStepCounter;
        private readonly IconGlyph _txtIcon;
        private readonly TextBlock _txtTitle;
        private readonly TextBlock _txtDescription;
        private readonly Button _btnSupport;
        private readonly StackPanel _followUpPanel;
        private readonly Button _btnFollowUp1;
        private readonly Button _btnFollowUp2;
        private readonly Button _btnFollowUp3;
        private readonly Button _btnSkip;
        private readonly Button _btnSkipStep;
        private readonly Button _btnPrevious;
        private readonly Button _btnNext;

        // ---- State ------------------------------------------------------------------

        /// <summary>The window the spotlight measures against. Null in render/sample mode, which
        /// is also what keeps <see cref="UpdateOverlayPosition"/> from resizing the window under
        /// the headless render.</summary>
        private Window? _targetWindow;

        private TutorialStep? _step;
        private bool _loaded;

        /// <summary>True when this overlay is driving a real tour off <c>CoreTutorial</c>. False in
        /// the render constructor, which owns its own sample step and must not ask the seam
        /// anything — an unseeded seam would answer "0 of 0" and blank the sample card.</summary>
        private readonly bool _live;

        /// <summary>Once-per-step latch for <see cref="Advance"/>, exactly as WPF's
        /// <c>_advanceFiredThisStep</c>: overlay-instance state, reset on every step change. The
        /// brief called this portable; the original disagrees and it stays here, because the thing
        /// being de-duplicated is one overlay's view of one card, not the service's cursor.</summary>
        private bool _advanceFiredThisStep;

        /// <summary>Sample spotlight rect used by the render constructor, so the PNG exercises the
        /// spotlight path (geometry + glow ring + panel placement) and not just the plain dim.</summary>
        private Rect? _sampleTargetBounds;

        /// <summary>The counter text the render constructor shows, since the seam has no tour to
        /// count. Null on the live overlay, where the real "Step n of m" always wins.</summary>
        private string? _sampleCounter;

        // Retry timer used by UpdateSpotlight when the target's bounds aren't measured yet. Held
        // so consecutive UpdateSpotlight calls can cancel the prior tick (otherwise they pile up
        // and fire stale layouts) and so teardown stops it on close.
        private DispatcherTimer? _spotlightDelayTimer;
        private Rect? _inputHole;
        private bool _cardInputOnly;
        internal bool HasClosed => _closed;
        internal PixelRect[]? InputRectangles { get; private set; }

        /// <summary>
        /// Render constructor. Sample step data, and NOT connected to the seam: RenderProof needs a
        /// parameterless ctor, and a headless render must not be able to walk a live tour.
        /// </summary>
        public TutorialOverlay() : this(null, live: false)
        {
            // Placeholder: the first step of the full tour, re-pointed at a sample rect so the
            // render draws a spotlight rather than the Center-positioned plain card.
            _sampleCounter = "Step 1 of 6";
            _step = new TutorialStep
            {
                Id = "welcome",
                Icon = "~",
                Title = "Welcome to Conditioning Control Panel!",
                Description = "Everything lives behind seven doors down the left. This quick tour opens each " +
                              "one so you know where things are.\n\n" +
                              "You can replay it any time from the ? button in the title bar.",
                TextPosition = TutorialStepPosition.Bottom,
                TargetElementName = "SampleTarget",
                Advance = TutorialAdvanceTrigger.Manual,
                AllowManualSkip = true,
            };
            _sampleTargetBounds = new Rect(120, 90, 300, 64);
        }

        /// <summary>
        /// The live overlay: it draws whatever tour <c>CoreTutorial</c> is running and measures its
        /// spotlight against <paramref name="targetWindow"/>. Show it AFTER the tour has started,
        /// as WPF does (MainWindow.StartTutorial calls Start then constructs) — the first card is
        /// read on Loaded, and StepChanged carries every card after it.
        ///
        /// TutorialHead starts shell tours through this constructor.
        /// </summary>
        public TutorialOverlay(Window targetWindow) : this(targetWindow, live: true)
        {
        }

        private TutorialOverlay(Window? targetWindow, bool live)
        {
            AvaloniaXamlLoader.Load(this);

            _spotlightCanvas = this.FindControl<Canvas>("SpotlightCanvas")!;
            _textPanel = this.FindControl<Border>("TextPanel")!;
            _txtStepCounter = this.FindControl<TextBlock>("TxtStepCounter")!;
            _txtIcon = this.FindControl<IconGlyph>("TxtIcon")!;
            _txtTitle = this.FindControl<TextBlock>("TxtTitle")!;
            _txtDescription = this.FindControl<TextBlock>("TxtDescription")!;
            _btnSupport = this.FindControl<Button>("BtnSupport")!;
            _followUpPanel = this.FindControl<StackPanel>("FollowUpPanel")!;
            _btnFollowUp1 = this.FindControl<Button>("BtnFollowUp1")!;
            _btnFollowUp2 = this.FindControl<Button>("BtnFollowUp2")!;
            _btnFollowUp3 = this.FindControl<Button>("BtnFollowUp3")!;
            _btnSkip = this.FindControl<Button>("BtnSkip")!;
            _btnSkipStep = this.FindControl<Button>("BtnSkipStep")!;
            _btnPrevious = this.FindControl<Button>("BtnPrevious")!;
            _btnNext = this.FindControl<Button>("BtnNext")!;

            _btnSupport.Click += BtnSupport_Click;
            _btnFollowUp1.Click += (_, _) => InvokeFollowUp(1);
            _btnFollowUp2.Click += (_, _) => InvokeFollowUp(2);
            _btnFollowUp3.Click += (_, _) => InvokeFollowUp(3);
            _btnSkip.Click += (_, _) => SkipTutorial();
            _btnSkipStep.Click += (_, _) => Advance();
            _btnPrevious.Click += (_, _) => PreviousStep();
            _btnNext.Click += (_, _) => NextStep();

            LayoutUpdated += (_, _) => RefreshWindowInputRegion();
            KeyDown += OnKeyDown;
            Focusable = true;

            _live = live;
            _targetWindow = targetWindow;

            if (_live)
            {
                if (_targetWindow != null) _targetWindow.Closed += OnTargetClosed;
                CoreTutorial.StepChanged += OnSeamStepChanged;
                CoreTutorial.Finished += OnSeamFinished;
                CoreTutorialEvents.Event += OnTutorialEvent;
            }

            Loaded += (_, _) =>
            {
                _loaded = true;
                UpdateOverlayPosition();
                // Live: the tour has already started, so the first card is read here (WPF does the
                // same on its own Loaded). Sample: _step was set by the render constructor.
                var first = _live ? CoreTutorial.CurrentStep : _step;
                if (first != null) UpdateStep(first);
            };
        }

        /// <summary>The tour moved. Raised synchronously by the head that owns it, and possibly off
        /// the UI thread, so it hops back before touching a control.</summary>
        private void OnSeamStepChanged(object? sender, TutorialStep step)
        {
            if (Dispatcher.UIThread.CheckAccess()) UpdateStep(step);
            else Dispatcher.UIThread.Post(() => UpdateStep(step));
        }

        /// <summary>The tour ended, by any route. The overlay goes with it — WPF fades out over
        /// 200ms and closes; the fade is dropped here for the reason in the header.</summary>
        private void OnSeamFinished(object? sender, bool completed)
        {
            _ = completed;
            if (Dispatcher.UIThread.CheckAccess()) Close();
            else Dispatcher.UIThread.Post(Close);
        }

        private void OnTargetClosed(object? sender, EventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            UnsubscribeAdvanceTrigger();
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;

            if (_live)
            {
                if (_targetWindow != null) _targetWindow.Closed -= OnTargetClosed;
                CoreTutorial.StepChanged -= OnSeamStepChanged;
                CoreTutorial.Finished -= OnSeamFinished;
                CoreTutorialEvents.Event -= OnTutorialEvent;

                // The window going away while the tour is still running is exactly what WPF's
                // OnAppExit / OnMainWindowClosed handlers cover, and they Skip() for a reason: a
                // tour with no overlay left is not running, and an IsActive stuck true would shut
                // every "not while a tutorial is up" gate in the app for the rest of the session.
                // Skip never latches the tour as completed, and the head guards re-entry, so the
                // ordinary close-after-Finished path lands on a no-op.
                if (CoreTutorial.IsActive) CoreTutorial.Skip();
            }

            base.OnClosed(e);
        }

        /// <summary>A step's Icon string is data (WPF drew the emoji): mapped through IconMap to a Fluent
        /// icon, and anything unmapped (">", "~", "<3") falls back to Sparkle, never the emoji text.</summary>
        internal static (IconKind Kind, IconVariant Variant) IconFor(string? icon) =>
            IconMap.TryGet(icon, out var e) ? (e.Kind, e.Variant) : (IconKind.Sparkle, IconVariant.Regular);

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SkipTutorial();
                e.Handled = true;
            }
        }

        // ---- Position over the target's monitor -------------------------------------

        /// <summary>
        /// Cover the whole monitor the target is on. WPF read Screen.FromHandle plus the WPF DPI
        /// scale because Window.Left is unreliable for a maximized WindowStyle="None" window;
        /// Avalonia's Screens gives the same answer without a P/Invoke, so
        /// GetDpiForMonitor / MonitorFromPoint / SystemParameters.WorkArea are all gone. The
        /// union-of-window-bounds fallback goes with them: it existed only for a failed Win32
        /// lookup.
        /// </summary>
        private void UpdateOverlayPosition()
        {
            // No target means render/sample mode: leave the window at the size the render proof
            // chose instead of snapping it to the headless screen.
            if (_targetWindow is not null)
            {
                try
                {
                    var screen = Screens.ScreenFromWindow(_targetWindow) ?? Screens.Primary;
                    if (screen is not null)
                    {
                        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                        Position = screen.Bounds.Position;
                        Width = screen.Bounds.Width / scale;
                        Height = screen.Bounds.Height / scale;
                    }
                }
                catch { /* no screen info; keep the current bounds */ }
            }

            if (_step != null && _loaded) UpdateSpotlight(_step);
        }

        // ---- Step rendering ---------------------------------------------------------

        private void UpdateStep(TutorialStep step)
        {
            _step = step;

            // ponytail: cross-window retarget and WindowLoaded fallback are still in
            // wire-win-tutorial-overlay-b. This slice binds to the current host only.
            _advanceFiredThisStep = false;

            _txtStepCounter.Text = StepCounterText();
            (_txtIcon.Kind, _txtIcon.Variant) = IconFor(step.Icon);
            _txtTitle.Text = step.Title;
            _txtDescription.Text = step.Description;

            _btnSupport.IsVisible = step.Id == "support";
            _btnSkip.IsVisible = !step.IsFollowUpCard;
            _btnSkipStep.IsVisible = step.AllowManualSkip && !step.IsFollowUpCard;

            // Manual advance trigger -> show Next; otherwise auto-advance handles it.
            bool isManual = step.Advance == TutorialAdvanceTrigger.Manual;
            _btnNext.IsVisible = isManual && !step.IsFollowUpCard;
            // "Finish" only when the seam says there IS a last card to be on: unseeded, TotalSteps
            // is 0 and IsLastStep is false, so the sample card keeps the honest "Next".
            _btnNext.Content = _live && CoreTutorial.IsLastStep ? "Finish" : "Next";

            // Hide Previous in rails mode (going back is messy with state) and on follow-up cards.
            // On the sample card there is nowhere to go back to either, so it hides there too.
            _btnPrevious.IsVisible = isManual && !step.IsFollowUpCard && _live && !CoreTutorial.IsFirstStep;

            // Follow-up card mode renders a stacked button list inside the panel.
            if (step.IsFollowUpCard)
            {
                _followUpPanel.IsVisible = true;
                ConfigureFollowUpButton(_btnFollowUp1, step.FollowUp1Text, step.FollowUp1);
                ConfigureFollowUpButton(_btnFollowUp2, step.FollowUp2Text, step.FollowUp2);
                ConfigureFollowUpButton(_btnFollowUp3, step.FollowUp3Text, step.FollowUp3);
            }
            else
            {
                _followUpPanel.IsVisible = false;
            }

            // Run the spotlight synchronously rather than deferring it: on Windows the region that
            // lets clicks reach the highlighted control MUST be in place before the user's
            // MouseDown, or the button below never sets IsPressed and its Click never fires.
            // UpdateSpotlight has its own timer-based retry for the "target not laid out yet" case.
            try { UpdateSpotlight(step); } catch { /* a step must never take the window down */ }

            // Deferred, exactly as in WPF: UpdateStep can run inside the target's own click
            // handling, and focusing here would steal keyboard focus mid-click and break the
            // button's Click sequence. This is what makes Escape -> Skip reachable.
            Dispatcher.UIThread.Post(() =>
            {
                try { Focus(); } catch { /* window already closing */ }
            }, DispatcherPriority.Background);

            if (_live) SubscribeAdvanceTrigger(step);
        }

        /// <summary>The follow-up label comes from step data, which may contain an underscore, so
        /// it goes in a TextBlock: Avalonia parses "_" in a Button's string Content as an access
        /// key and would swallow it (CLAUDE.md trap 1).</summary>
        private static void ConfigureFollowUpButton(Button btn, string? text, Action? handler)
        {
            if (string.IsNullOrEmpty(text) || handler == null)
            {
                btn.IsVisible = false;
                return;
            }
            btn.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            btn.IsVisible = true;
        }

        // ---- Spotlight --------------------------------------------------------------

        private void UpdateSpotlight(TutorialStep step)
        {
            // Cancel any retry from a previous step — otherwise rapid step changes queue up
            // overlapping ticks that paint stale bounds over the new step's layout.
            try { _spotlightDelayTimer?.Stop(); } catch { /* already stopped */ }
            _spotlightDelayTimer = null;

            _spotlightCanvas.Children.Clear();

            // The sidebar keeps a door's entries collapsed until the door is open, so any step that
            // needs a tab must open that door FIRST — a collapsed entry has no measurable bounds.
            bool doorOpened = PrepareDoorForStep(step);

            // Click-through hole only when the step is gated on a specific element interaction.
            // Manual steps and follow-up cards block all clicks (full opaque overlay).
            bool clickThroughHole = step.Advance != TutorialAdvanceTrigger.Manual &&
                                    !step.IsFollowUpCard;

            if (step.IsFollowUpCard ||
                step.TargetElementName == null ||
                step.TextPosition == TutorialStepPosition.Center)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                return;
            }

            // Sample mode: no target window to measure, so draw the seeded rect. This is the path
            // the render proof takes.
            if (_targetWindow is null)
            {
                if (_sampleTargetBounds is { } sample)
                {
                    DrawSpotlightOverlay(sample, clickThroughHole);
                    PositionTextPanel(sample, step.TextPosition);
                }
                else
                {
                    DrawFullOverlay(step.BlockBackgroundClicks);
                    CenterTextPanel();
                }
                return;
            }

            try { Tours.TutorialHead.CurrentHeadStep?.PrepareTargetWindowAction?.Invoke(_targetWindow); }
            catch { /* tutorial prep must not prevent Skip */ }
            try { _targetWindow.UpdateLayout(); } catch { /* not laid out yet */ }

            var targetElement = FindElementByName(_targetWindow, step.TargetElementName);
            if (targetElement == null)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
                return;
            }

            // A Manual step pointing at a TextBox almost always means "type something here, then
            // click Next." Without click-through the overlay swallows the click and the box never
            // gets focus.
            if (targetElement is TextBox) clickThroughHole = true;

            try { targetElement.BringIntoView(); } catch { /* not in a scroller */ }
            try { _targetWindow.UpdateLayout(); } catch { /* not laid out yet */ }

            var bounds = GetElementBounds(targetElement);
            bool unmeasured = bounds.X == 0 && bounds.Y == 0 && bounds.Width <= 100;

            if (unmeasured)
            {
                DrawFullOverlay(step.BlockBackgroundClicks);
                CenterTextPanel();
            }
            else
            {
                DrawSpotlightOverlay(bounds, clickThroughHole);
                PositionTextPanel(bounds, step.TextPosition);
            }

            // Re-measure when the target wasn't laid out yet, and also whenever we just opened a
            // door: an accordion animates, so the entry's bounds keep moving for a few frames
            // after the door "is" open and the first measure would pin the spotlight to a stale
            // rect.
            if (unmeasured || doorOpened)
            {
                ScheduleSpotlightSettle(step, targetElement, clickThroughHole, allowDegenerateDraw: unmeasured);
            }
        }

        /// <summary>
        /// Polls the target's bounds a few times and redraws the spotlight until they stop moving.
        /// Replaces the old single 120ms retry: same first tick, but it now converges instead of
        /// painting whatever the one shot happened to catch.
        /// </summary>
        private void ScheduleSpotlightSettle(TutorialStep currentStep, Control targetElement,
                                             bool clickThroughHole, bool allowDegenerateDraw)
        {
            const int maxTicks = 4;
            int ticks = 0;
            Rect lastBounds = default;

            // Held in a local as well as the field: a later step replaces the field, and this
            // closure must always stop ITS OWN timer, never the successor's.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _spotlightDelayTimer = timer;

            void StopSelf()
            {
                try { timer.Stop(); } catch { /* already stopped */ }
                if (ReferenceEquals(_spotlightDelayTimer, timer)) _spotlightDelayTimer = null;
            }

            timer.Tick += (_, _) =>
            {
                ticks++;
                bool last = ticks >= maxTicks;

                if (!ReferenceEquals(_step, currentStep))
                {
                    StopSelf();
                    return;
                }

                var b = GetElementBounds(targetElement);
                bool degenerate = b.X == 0 && b.Y == 0 && b.Width <= 100;

                // Keep a good spotlight rather than replacing it with a bad measurement; only the
                // never-measured case is allowed to draw a degenerate rect (old behaviour).
                if (!degenerate || (allowDegenerateDraw && last))
                {
                    _spotlightCanvas.Children.Clear();
                    DrawSpotlightOverlay(b, clickThroughHole);
                    PositionTextPanel(b, currentStep.TextPosition);
                }

                // Settled (two identical measurements) or out of tries.
                if (last || (!degenerate && b == lastBounds))
                {
                    StopSelf();
                    return;
                }
                lastBounds = b;
            };
            timer.Start();
        }

        /// <summary>Opens the sidebar door that owns this step's target before we try to measure
        /// it. Returns true when a door was addressed (the caller then re-measures once it
        /// settles).</summary>
        private bool PrepareDoorForStep(TutorialStep step)
        {
            // ponytail: needs MainShellWindow.ExpandDoorForTab, which does not exist on this head
            // (MainShellWindow.TabNavigation.cs has ShowTab, which also NAVIGATES — and a step can
            // spotlight a nav entry filed under a door other than the tab it requires, so ShowTab
            // would jump the user off the page the card is talking about). The door key itself is
            // deliberately not on CoreTutorial.Step either: TutorialService.DoorTabKeyFor is a pure
            // function of the step, but with no way to act on the answer a field carrying it would
            // just be seam surface nothing reads.
            //
            // Until that method exists, no door is opened, so a step whose target sits behind a
            // collapsed door falls through to the full-overlay path below — the same degraded card
            // WPF draws when an element cannot be found.
            _ = step;
            return false;
        }

        private void DrawFullOverlay(bool blockClicks = true)
        {
            // When blockClicks=false the user needs to interact with something ON TOP of our
            // overlay (e.g. an OS save dialog), so drop the dim entirely and hand the whole
            // window's input away; the card alone signals the wait state.
            byte alpha = blockClicks ? DimAlpha() : (byte)0x00;
            var overlay = new Rectangle
            {
                Width = Bounds.Width,
                Height = Bounds.Height,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0x00, 0x00)),
                IsHitTestVisible = blockClicks,
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            _spotlightCanvas.Children.Add(overlay);

            _inputHole = null;
            _cardInputOnly = !blockClicks;
            RefreshWindowInputRegion();
        }

        private void DrawSpotlightOverlay(Rect highlightBounds, bool clickThroughHole)
        {
            var padding = 8.0;
            var glowBounds = new Rect(
                highlightBounds.X - padding,
                highlightBounds.Y - padding,
                highlightBounds.Width + padding * 2,
                highlightBounds.Height + padding * 2
            );

            var fullRect = new RectangleGeometry(new Rect(0, 0, Bounds.Width, Bounds.Height));
            var spotlightRect = new RectangleGeometry(glowBounds) { RadiusX = 8, RadiusY = 8 };

            // When clickThroughHole=true the dark fill is the full rect MINUS the spotlight rect,
            // so the hole has no geometry and clicks fall through to the underlying control.
            // Otherwise a full opaque rect blocks everything (standard manual-mode overlay).
            Geometry darkGeometry = clickThroughHole
                ? new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, spotlightRect)
                : fullRect;

            var darkPath = new Path
            {
                Data = darkGeometry,
                Fill = new SolidColorBrush(Color.FromArgb(DimAlpha(), 0x00, 0x00, 0x00)),
                IsHitTestVisible = true,
            };
            _spotlightCanvas.Children.Add(darkPath);

            var glowBorder = new Border
            {
                Width = glowBounds.Width,
                Height = glowBounds.Height,
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                    BlurRadius = 15,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = 0.7,
                },
            };
            Canvas.SetLeft(glowBorder, glowBounds.X);
            Canvas.SetTop(glowBorder, glowBounds.Y);
            _spotlightCanvas.Children.Add(glowBorder);

            if (clickThroughHole) ApplyWindowSpotlightRegion(highlightBounds);
            else ClearWindowSpotlightRegion();
        }

        /// <summary>WPF dimmed harder over MainWindow (0xA0) than over a dialog (0x70). Sample mode
        /// has no target, and the overlay's normal home is MainWindow, so it takes the same
        /// value.</summary>
        private byte DimAlpha() => _targetWindow is null or MainWindow or MainShellWindow ? (byte)0xA0 : (byte)0x70;

        // ---- Window-level click-through ---------------------------------------------

        private void ApplyWindowSpotlightRegion(Rect spotlightBoundsDip)
        {
            _cardInputOnly = false;
            _inputHole = spotlightBoundsDip;
            RefreshWindowInputRegion();
        }

        private void ClearWindowSpotlightRegion()
        {
            _cardInputOnly = false;
            _inputHole = null;
            RefreshWindowInputRegion();
        }

        private void RefreshWindowInputRegion()
        {
            PixelRect[]? rects = null;
            double scale = RenderScaling;
            if (_inputHole is Rect hole)
            {
                // An unset Width/Height is NaN, and Math.Max(x, NaN) is NaN: fall back to Bounds.
                rects = SpotlightInputRegion.Create(Extent(Bounds.Width, Width), Extent(Bounds.Height, Height),
                    scale, scale, hole.X, hole.Y, hole.Width, hole.Height)
                    .Select(r => new PixelRect(r.X, r.Y, r.Width, r.Height)).ToArray();
                // No measurable window yet: keep all input rather than an empty region that would
                // also swallow the card's Skip buttons.
                if (rects.Length == 0) rects = null;
            }
            else if (_cardInputOnly)
            {
                var position = _textPanel.TranslatePoint(default, this);
                if (position is not Point p || _textPanel.Bounds.Width <= 0 || _textPanel.Bounds.Height <= 0)
                    return; // Wait for layout; keep the card's exit buttons reachable.
                rects = new[] { PixelRect.FromRect(new Rect(p, _textPanel.Bounds.Size), scale) };
            }
            if (rects is null && InputRectangles is null ||
                rects is not null && InputRectangles is not null && rects.SequenceEqual(InputRectangles)) return;
            InputRectangles = rects;
            X11Overlay.SetWindowInputRegion(this, rects);

            static double Extent(double laidOut, double set) => double.IsNaN(set) ? laidOut : Math.Max(laidOut, set);
        }

        // ---- Card placement ---------------------------------------------------------

        private void PositionTextPanel(Rect targetBounds, TutorialStepPosition position)
        {
            _textPanel.HorizontalAlignment = HorizontalAlignment.Left;
            _textPanel.VerticalAlignment = VerticalAlignment.Top;

            _textPanel.UpdateLayout();
            var panelWidth = _textPanel.Bounds.Width > 0 ? _textPanel.Bounds.Width : 460;
            var panelHeight = _textPanel.Bounds.Height > 0 ? _textPanel.Bounds.Height : 220;

            const double margin = 20;

            // Center the panel on the target along the perpendicular axis. With small targets like
            // buttons (width 120) and a much wider card (~460), edge-aligning made the card look
            // "offset" — its center floated way off the target's center.
            var (left, top) = ComputePanelPosition(position, targetBounds, panelWidth, panelHeight, margin);

            // Clamp to overlay extent.
            double clampedLeft = Math.Max(margin, Math.Min(left, Bounds.Width - panelWidth - margin));
            double clampedTop = Math.Max(margin, Math.Min(top, Bounds.Height - panelHeight - margin));

            // If clamping moved us into / over the target (small overlay or target near an edge),
            // flip to the opposite side so the card never sits on top of the spotlighted control.
            var panelRect = new Rect(clampedLeft, clampedTop, panelWidth, panelHeight);
            if (panelRect.Intersects(targetBounds))
            {
                var flipped = FlipPosition(position);
                var (altLeft, altTop) = ComputePanelPosition(flipped, targetBounds, panelWidth, panelHeight, margin);
                double altClampedLeft = Math.Max(margin, Math.Min(altLeft, Bounds.Width - panelWidth - margin));
                double altClampedTop = Math.Max(margin, Math.Min(altTop, Bounds.Height - panelHeight - margin));
                var altRect = new Rect(altClampedLeft, altClampedTop, panelWidth, panelHeight);
                if (!altRect.Intersects(targetBounds))
                {
                    clampedLeft = altClampedLeft;
                    clampedTop = altClampedTop;
                }
            }

            _textPanel.Margin = new Thickness(clampedLeft, clampedTop, 0, 0);
        }

        private static (double left, double top) ComputePanelPosition(
            TutorialStepPosition position, Rect targetBounds,
            double panelWidth, double panelHeight, double margin)
        {
            double left = 0, top = 0;
            switch (position)
            {
                case TutorialStepPosition.Bottom:
                    left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                    top = targetBounds.Bottom + margin;
                    break;
                case TutorialStepPosition.Top:
                    left = targetBounds.X + (targetBounds.Width - panelWidth) / 2;
                    top = targetBounds.Top - panelHeight - margin;
                    break;
                case TutorialStepPosition.Left:
                    left = targetBounds.Left - panelWidth - margin;
                    top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                    break;
                case TutorialStepPosition.Right:
                    left = targetBounds.Right + margin;
                    top = targetBounds.Y + (targetBounds.Height - panelHeight) / 2;
                    break;
            }
            return (left, top);
        }

        private static TutorialStepPosition FlipPosition(TutorialStepPosition p) => p switch
        {
            TutorialStepPosition.Top => TutorialStepPosition.Bottom,
            TutorialStepPosition.Bottom => TutorialStepPosition.Top,
            TutorialStepPosition.Left => TutorialStepPosition.Right,
            TutorialStepPosition.Right => TutorialStepPosition.Left,
            _ => TutorialStepPosition.Bottom
        };

        private void CenterTextPanel()
        {
            _textPanel.HorizontalAlignment = HorizontalAlignment.Center;
            // MainShellWindow beside MainWindow: on this head the app shell is the tour's home
            // and the diagnostics MainWindow is not, so the centred card must follow the shell.
            if (_targetWindow is null or MainWindow or MainShellWindow)
            {
                _textPanel.VerticalAlignment = VerticalAlignment.Center;
                _textPanel.Margin = new Thickness(0);
            }
            else
            {
                _textPanel.VerticalAlignment = VerticalAlignment.Bottom;
                _textPanel.Margin = new Thickness(0, 0, 0, 30);
            }
        }

        // ---- Target lookup ----------------------------------------------------------

        /// <summary>WPF walked the visual tree with VisualTreeHelper after trying FindName; the
        /// Avalonia twin is GetVisualChildren after FindControl.</summary>
        private static Control? FindElementByName(Visual? parent, string name)
        {
            if (parent == null) return null;

            if (parent is Control fe)
            {
                if (fe.Name == name) return fe;
                try
                {
                    var found = fe.FindControl<Control>(name);
                    if (found != null) return found;
                }
                catch (InvalidOperationException) { /* dynamic controls may have no name scope; walk the visual tree */ }
            }

            try
            {
                foreach (var child in parent.GetVisualChildren())
                {
                    if (child is Control element && element.Name == name)
                        return element;

                    var result = FindElementByName(child, name);
                    if (result != null) return result;
                }
            }
            catch { /* the tree changed under us; a missed target just means a full overlay */ }
            return null;
        }

        /// <summary>The target's bounds in this overlay's own coordinates. PointToScreen returns a
        /// PixelPoint on Avalonia (WPF returned a device-independent Point), and PointToClient is
        /// the inverse — the pair replaces WPF's PointToScreen / PointFromScreen.</summary>
        private Rect GetElementBounds(Control element)
        {
            try
            {
                var screenTopLeft = element.PointToScreen(new Point(0, 0));
                var overlayLocal = this.PointToClient(screenTopLeft);
                return new Rect(overlayLocal, element.Bounds.Size);
            }
            catch
            {
                return new Rect(0, 0, 100, 40);
            }
        }

        // ---- Button handlers --------------------------------------------------------

        /// <summary>"Step n of m" from the seam. The sample card has no tour behind it, so it keeps
        /// the placeholder the render constructor set rather than reading 0 of 0.</summary>
        private string StepCounterText()
        {
            if (!_live) return _sampleCounter ?? "";
            return $"Step {CoreTutorial.CurrentStepIndex + 1} of {CoreTutorial.TotalSteps}";
        }

        private void NextStep() => CoreTutorial.Next();
        private void PreviousStep() => CoreTutorial.Previous();
        private void SkipTutorial() => CoreTutorial.Skip();

        /// <summary>
        /// "Skip step", and the entry point an auto-advance trigger would use. Once per step, then
        /// deferred: WPF cannot run Next() synchronously here because UpdateStep would then rewrite
        /// the card in the middle of the click that asked for it, and the bubbling half of that
        /// click never reaches the button underneath. The latch is set synchronously, so a second
        /// press before the post lands does nothing.
        /// </summary>
        private void Advance()
        {
            if (_advanceFiredThisStep) return;
            _advanceFiredThisStep = true;
            UnsubscribeAdvanceTrigger();
            var step = _step;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && CoreTutorial.IsActive && ReferenceEquals(CoreTutorial.CurrentStep, step)) CoreTutorial.Next();
            });
        }

        /// <summary>Runs the live step's follow-up action, not the card's — a branch card can be
        /// clicked after the tour has moved on, and WPF reads CurrentStep here for that reason.
        /// Falls back to the drawn step when no tour is running (the render's sample card).</summary>
        private void InvokeFollowUp(int which)
        {
            var step = (_live ? CoreTutorial.CurrentStep : null) ?? _step;
            if (step is null) return;
            var action = which switch
            {
                1 => step.FollowUp1,
                2 => step.FollowUp2,
                _ => step.FollowUp3,
            };
            action?.Invoke();
        }

        /// <summary>Not a stub: UseShellExecute hands the URL to xdg-open on Linux exactly as it
        /// hands it to the shell on Windows.</summary>
        private void BtnSupport_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                Platform.ExternalOpener.Open("https://linktr.ee/CodeBambi");
            }
            catch { /* no browser, or the user has no shell handler */ }
        }
    }
}
