// WPF MainWindow.ChromeFx.cs:204-460; finite tab entrances and first-visit door pulses.
// The outgoing image preserves the 100 ms fade without reactivating a hidden view's services.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private DispatcherTimer? _tabTransitionTimer;
        private readonly Stopwatch _tabTransitionTime = new();
        private Control? _enteringTab;
        private TranslateTransform? _tabSlide;
        private (double X, double Y) _tabOffset;
        private readonly List<(Control Card, TranslateTransform? Slide)> _tabCards = new();
        private Image? _outgoingTabImage;
        private RenderTargetBitmap? _outgoingTabBitmap;
        internal Control? EnteringTab => _enteringTab;

        // Called BEFORE hiding panels. Native browser tabs cut immediately, exactly as WPF.
        private void PrepareTabTransition(Control? incoming)
        {
            CompleteTabTransition();
            if (!AmbientFxCanvas.Env.AllowTransitions || ChromeFxNav.IsAirspaceTab(CurrentTab)
                || !TabPanels.TryGetValue(CurrentTab, out var name)
                || Named<Control>(name) is not { IsVisible: true, Parent: Panel host } outgoing
                || outgoing == incoming || outgoing.Bounds.Width <= 0 || outgoing.Bounds.Height <= 0) return;
            try
            {
                var size = outgoing.Bounds.Size;
                var scale = RenderScaling;
                _outgoingTabBitmap = new RenderTargetBitmap(
                    new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
                    new Vector(96 * scale, 96 * scale));
                _outgoingTabBitmap.Render(outgoing);
                // ponytail: snapshot rather than re-showing the real tab; IsVisible owns service teardown here.
                _outgoingTabImage = new Image
                {
                    Source = _outgoingTabBitmap, Width = size.Width, Height = size.Height,
                    Margin = outgoing.Margin, HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                };
                host.Children.Add(_outgoingTabImage);
            }
            catch (Exception ex) { ClearOutgoingTabImage(); Log.Debug("Tab snapshot: {E}", ex.Message); }
        }

        private void AnimateTabIn(Control? tab, string key)
        {
            if (tab == null) return;
            tab.IsHitTestVisible = true;
            tab.Opacity = 1;
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            _enteringTab = tab;
            if (AmbientFxCanvas.Env.Level == MotionLevel.Full && !ChromeFxNav.IsAirspaceTab(key))
            {
                _tabSlide = EnsureTabTranslate(tab);
                _tabOffset = ChromeFxNav.EntranceOffset(CurrentTab, key, 12);
                foreach (var card in FindStaggerTargets(tab)) _tabCards.Add((card, EnsureTabTranslate(card)));
            }
            StepTabTransition(0);
            _tabTransitionTime.Restart();
            _tabTransitionTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
                (_, _) => StepTabTransition(_tabTransitionTime.Elapsed.TotalMilliseconds));
            _tabTransitionTimer.Start();
        }

        // P08: pointer tests settle the actual entrance before reading hit geometry. No wall-clock sleeps.
        internal void CompleteTabTransition() => StepTabTransition(620);

        internal void StepTabTransition(double ms)
        {
            static double Ease(double elapsed, double duration)
            {
                var t = Math.Clamp(elapsed / duration, 0, 1);
                return 1 - (1 - t) * (1 - t);
            }
            if (_outgoingTabImage != null)
            {
                _outgoingTabImage.Opacity = Math.Clamp(1 - ms / 100, 0, 1);
                if (ms >= 100) ClearOutgoingTabImage();
            }
            if (_enteringTab != null) _enteringTab.Opacity = Ease(ms, 200);
            if (_tabSlide != null)
            {
                var remaining = 1 - Ease(ms, 200);
                _tabSlide.X = _tabOffset.X * remaining;
                _tabSlide.Y = _tabOffset.Y * remaining;
            }
            for (var i = 0; i < _tabCards.Count; i++)
            {
                var (card, slide) = _tabCards[i];
                var elapsed = ms - 40 * Math.Min(i, 6);
                // WPF's delayed animation leaves the card at its base value until BeginTime.
                card.Opacity = elapsed < 0 ? 1 : Ease(elapsed, 220);
                if (slide != null) slide.Y = elapsed < 0 ? 0 : 10 * (1 - Ease(elapsed, 260));
            }
            if (ms < 620) return; // WPF cleanup: 6*40 + 260 + 120 ms
            _tabTransitionTimer?.Stop();
            _tabTransitionTime.Stop();
            _tabCards.Clear();
            _tabSlide = null;
            _enteringTab = null;
        }

        private void ClearOutgoingTabImage()
        {
            if (_outgoingTabImage?.Parent is Panel host) host.Children.Remove(_outgoingTabImage);
            if (_outgoingTabImage != null) _outgoingTabImage.Source = null;
            _outgoingTabImage = null;
            _outgoingTabBitmap?.Dispose();
            _outgoingTabBitmap = null;
        }

        private static TranslateTransform? EnsureTabTranslate(Control control)
        {
            if (control.RenderTransform is TranslateTransform existing) return existing;
            if (control.RenderTransform != null) return null; // preserve authored transforms
            var slide = new TranslateTransform();
            control.RenderTransform = slide;
            return slide;
        }

        internal static List<Control> FindStaggerTargets(Control? node)
        {
            for (var depth = 0; depth < 8 && node != null; depth++)
            {
                switch (node)
                {
                    case ContentControl { Content: Control inner }: node = inner; continue;
                    case Decorator { Child: { } child }: node = child; continue;
                    case Panel panel:
                        var cards = panel.Children.Where(c => c.IsVisible && c.Opacity > 0.99).ToList();
                        if (cards.Count == 1) { node = cards[0]; continue; }
                        return cards.Take(8).ToList();
                    default: return new();
                }
            }
            return new();
        }

        // ============================== first-visit door pulse ==============================

        private readonly Dictionary<string, (Button Header, CancellationTokenSource Run)> _navHeaderPulses =
            new(StringComparer.Ordinal);

        /// <summary>True while the rail row is announcing <paramref name="tabKey"/> (test seam).</summary>
        internal bool NavDoorHeaderPulsing(string tabKey) => _navHeaderPulses.ContainsKey(tabKey);

        private bool _firstVisitPulsesArmed;

        /// <summary>Stepped-clock seam (P08): tests capture the 1.2 s arming instead of waiting it out.</summary>
        internal Func<Action, TimeSpan, IDisposable> ScheduleOnce { get; set; } =
            (action, delay) => DispatcherTimer.RunOnce(action, delay);

        /// <summary>WPF MainWindow.xaml.cs:3795-3830, on load: 1.2 s later the Deeper and Programs
        /// rail rows announce a page the user has not opened yet. Opened fires on every Show, so armed once.</summary>
        private void InitializeFirstVisitPulses()
        {
            IDisposable? delay = null;
            Opened += (_, _) =>
            {
                if (_firstVisitPulsesArmed) return;
                _firstVisitPulsesArmed = true;
                delay = ScheduleOnce(StartFirstVisitPulses, TimeSpan.FromMilliseconds(1200));
            };
            // Nothing of this window's motion may outlive it (P65): the pending start, the rows, the entrance.
            Closed += (_, _) =>
            {
                delay?.Dispose();
                foreach (var key in _navHeaderPulses.Keys.ToList()) StopNavDoorHeaderPulse(key);
                CompleteTabTransition();
            };
        }

        /// <summary>The two independent first-visit checks (WPF xaml.cs:3797 Deeper, :3816 Programs).</summary>
        internal void StartFirstVisitPulses()
        {
            var s = CoreSettings.Current;
            if (s.EnableDeeper && !s.HasSeenDeeperTab) StartNavDoorHeaderPulse("deeper");
            // WPF StartProgramsTabPulse (ProgramsTab.cs:54): a decorative loop, so it goes with the ambient clocks.
            if (!s.HasSeenProgramsTab && AmbientFxCanvas.Env.AllowAmbientLoops) StartNavDoorHeaderPulse("programs");
        }

        /// <summary>
        /// WPF StartNavDoorHeaderPulse (TabNavigation.cs:831): four soft opacity dips on the section
        /// row that owns <paramref name="tabKey"/>. False when that section is already the lit one;
        /// true when the row took it or the quiet window / Motion Off swallowed it.
        /// </summary>
        internal bool StartNavDoorHeaderPulse(string tabKey)
        {
            try
            {
                if (Platform.StartupLadder.IsQuiet) return true;
                var door = NavDoorForTab(tabKey);
                if (door == null) return false;
                Button? header = null;
                foreach (var name in NavSectionButtonNames)
                    if (Named<Button>(name) is { Tag: string tag } b && tag == door) { header = b; break; }
                if (header == null) return false;
                if (string.Equals(NavDoorForTab(CurrentTab), door, StringComparison.Ordinal)) return false;
                if (_navHeaderPulses.ContainsKey(tabKey)) return true;
                if (!AmbientFxCanvas.Env.AllowTransitions) return true;

                var run = new CancellationTokenSource();
                _navHeaderPulses[tabKey] = (header, run);
                var pulse = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(AmbientFxCanvas.Env.Level == MotionLevel.Reduced ? 350 : 700),
                    IterationCount = new IterationCount(8),   // WPF AutoReverse x RepeatBehavior(4)
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.35) } },
                    },
                };
                _ = RunNavDoorHeaderPulse(tabKey, header, run, pulse);
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("StartNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message);
                return false;
            }
        }

        private async Task RunNavDoorHeaderPulse(string tabKey, Button header, CancellationTokenSource run, Animation pulse)
        {
            try { await pulse.RunAsync(header, run.Token); }
            catch (Exception ex) { Log.Debug("NavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message); }
            if (_navHeaderPulses.TryGetValue(tabKey, out var p) && p.Run == run) StopNavDoorHeaderPulse(tabKey);
        }

        /// <summary>WPF StopNavDoorHeaderPulse (:863): release the animation, the row back at rest.</summary>
        internal void StopNavDoorHeaderPulse(string tabKey)
        {
            if (!_navHeaderPulses.Remove(tabKey, out var p)) return;
            try
            {
                p.Run.Cancel();
                p.Header.Opacity = 1.0;
            }
            catch (Exception ex) { Log.Debug("StopNavDoorHeaderPulse({Tab}): {E}", tabKey, ex.Message); }
        }
    }
}
