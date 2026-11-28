using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>One card of the assets nudge. <paramref name="Tab"/> is the ShowTab key the
    /// control lives on; <paramref name="Target"/> names the control the arrow points at
    /// (resolved by MainWindow, so this table stays pure).</summary>
    public sealed record AssetsNudgeStep(string Id, string Tab, string Target, string TitleKey, string TextKey);

    /// <summary>
    /// The assets nudge's cards and its one-time rule (2026-11-25, after a new member in #general
    /// could not find how to get pictures in or switch the Scrolller ones on). Pure, so the tests
    /// read the same table the card draws. Every target exists today: the Library source bar
    /// (My own assets / Scrolller / Both), the Folders zone with Pick folder (drag and drop onto
    /// the window works too), and the Start button at the foot of Home.
    /// </summary>
    public static class AssetsNudgePlan
    {
        public const string TargetSourceBar = "sourcebar";
        public const string TargetFolders = "folders";
        public const string TargetStart = "start";

        public static readonly IReadOnlyList<AssetsNudgeStep> Steps = new[]
        {
            new AssetsNudgeStep("source", "assets", TargetSourceBar, "assetsnudge_source_title", "assetsnudge_source_text"),
            new AssetsNudgeStep("add", "assets", TargetFolders, "assetsnudge_add_title", "assetsnudge_add_text"),
            new AssetsNudgeStep("enable", "settings", TargetStart, "assetsnudge_enable_title", "assetsnudge_enable_text"),
        };

        /// <summary>Offered once per profile. A fresh install gets it after Start here; an upgrade
        /// only when the library is still empty (local source, no pictures, no videos), because
        /// anyone with assets already knows the way. Never during Lockdown or Strict Lock: those
        /// sessions keep the screen they asked for.</summary>
        public static bool ShouldOffer(bool alreadyShown, bool ageAccepted, bool freshInstall,
                                       bool libraryEmpty, bool lockdownActive, bool strictLock)
        {
            if (alreadyShown || !ageAccepted) return false;
            if (lockdownActive || strictLock) return false;
            return freshInstall || libraryEmpty;
        }

        /// <summary>The arrow moves only when the app's motion setting is Full and Windows has
        /// client-area animation on; otherwise it is drawn still.</summary>
        public static bool ArrowMoves(Models.MotionLevel level, bool systemAnimations)
            => level == Models.MotionLevel.Full && systemAnimations;
    }

    /// <summary>
    /// A small owned card that sits next to one control with an arrow pointing at it. Non-modal
    /// and never activated, so it never takes the keyboard and never blocks the app. Next moves
    /// on, Skip (or the last Next) closes it. Replay lives in Help.
    /// </summary>
    public partial class AssetsNudgeCard : Window
    {
        private readonly IReadOnlyList<AssetsNudgeStep> _steps;
        private readonly Func<AssetsNudgeStep, FrameworkElement?> _resolve;
        private readonly bool _arrowMoves;
        private int _index;

        public AssetsNudgeCard(IReadOnlyList<AssetsNudgeStep> steps,
                               Func<AssetsNudgeStep, FrameworkElement?> resolve, bool arrowMoves)
        {
            InitializeComponent();
            _steps = steps;
            _resolve = resolve;
            _arrowMoves = arrowMoves;
            Loaded += (_, _) => ShowStep(0);
        }

        /// <summary>The card currently shown (0-based); for the tests and the log.</summary>
        internal int Index => _index;

        /// <summary>Fills the card's words for card <paramref name="i"/> (no navigation, no
        /// placement), so the render test draws exactly what a user reads.</summary>
        internal void FillStep(int i)
        {
            _index = i;
            var step = _steps[i];
            TxtTitle.Text = Loc.Get(step.TitleKey);
            TxtBody.Text = Loc.Get(step.TextKey);
            TxtCount.Text = $"{i + 1} / {_steps.Count}";
            BtnNext.Content = Loc.Get(i == _steps.Count - 1 ? "assetsnudge_done" : "assetsnudge_next");
            BtnSkip.Visibility = i == _steps.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Shows the arrow on one side, for the render test (placement does it live).</summary>
        internal void ShowArrow(bool up)
        {
            ArrowUp.Visibility = up ? Visibility.Visible : Visibility.Collapsed;
            ArrowDown.Visibility = up ? Visibility.Collapsed : Visibility.Visible;
            (up ? ArrowUp : ArrowDown).Margin = new Thickness(40, up ? 0 : -4, 0, up ? -4 : 0);
        }

        private void ShowStep(int i)
        {
            if (i >= _steps.Count) { Close(); return; }
            FillStep(i);
            var step = _steps[i];

            FrameworkElement? target = null;
            try { target = _resolve(step); }
            catch (Exception ex) { App.Logger?.Debug("Assets nudge: {Id} target failed: {E}", step.Id, ex.Message); }

            // The tab switch lays out on the next pass: place the card once it has.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => PlaceNear(target)));
        }

        private void PlaceNear(FrameworkElement? target)
        {
            if (Owner == null) return;
            ArrowUp.Visibility = Visibility.Collapsed;
            ArrowDown.Visibility = Visibility.Collapsed;
            UpdateLayout();

            Rect? box = TargetBox(target);
            var ownerBox = OwnerBox();
            if (box == null || ownerBox == null)
            {
                // No target on screen (a tab that failed to open): centre on the owner, no arrow.
                var o = ownerBox ?? new Rect(Owner.Left, Owner.Top, Owner.ActualWidth, Owner.ActualHeight);
                Left = o.Left + (o.Width - ActualWidth) / 2;
                Top = o.Top + (o.Height - ActualHeight) / 2;
                return;
            }

            var b = box.Value;
            var w = ActualWidth;
            // Below the target when it sits in the top half of the window, above it otherwise.
            bool below = b.Top + b.Height / 2 < ownerBox.Value.Top + ownerBox.Value.Height / 2;
            var arrow = below ? ArrowUp : ArrowDown;
            arrow.Visibility = Visibility.Visible;
            UpdateLayout();
            var h = ActualHeight;

            var left = Math.Max(ownerBox.Value.Left + 8,
                Math.Min(b.Left + b.Width / 2 - w / 2, ownerBox.Value.Right - w - 8));
            Left = left;
            Top = below ? b.Bottom + 2 : b.Top - h - 2;
            // The arrow's tip sits over the target's centre, clamped inside the card.
            var tipX = Math.Max(14, Math.Min(b.Left + b.Width / 2 - left - 13, w - 40));
            arrow.Margin = new Thickness(tipX, below ? 0 : -4, 0, below ? -4 : 0);

            var shift = below ? ArrowUpShift : ArrowDownShift;
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            if (_arrowMoves)
            {
                var bob = new DoubleAnimation(0, below ? -7 : 7, TimeSpan.FromMilliseconds(550))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                shift.BeginAnimation(TranslateTransform.YProperty, bob);
            }
        }

        /// <summary>The target's box in this window's coordinate space (DIPs on screen).</summary>
        private Rect? TargetBox(FrameworkElement? t)
        {
            if (t == null || !t.IsVisible || t.ActualWidth < 1 || t.ActualHeight < 1) return null;
            var src = PresentationSource.FromVisual(t);
            if (src?.CompositionTarget == null) return null;
            var fromDevice = src.CompositionTarget.TransformFromDevice;
            var tl = fromDevice.Transform(t.PointToScreen(new Point(0, 0)));
            var br = fromDevice.Transform(t.PointToScreen(new Point(t.ActualWidth, t.ActualHeight)));
            return new Rect(tl, br);
        }

        private Rect? OwnerBox()
        {
            if (Owner is not FrameworkElement root || !Owner.IsVisible) return null;
            return TargetBox(root) ?? new Rect(Owner.Left, Owner.Top, Owner.ActualWidth, Owner.ActualHeight);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e) => ShowStep(_index + 1);

        private void BtnSkip_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Escape is the panic key's job and reaches it on its own; the card closes with it.
            if (e.Key == Key.Escape) Close();
        }
    }
}
