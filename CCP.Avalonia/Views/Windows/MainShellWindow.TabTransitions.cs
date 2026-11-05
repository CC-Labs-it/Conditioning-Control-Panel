// WPF MainWindow.ChromeFx.cs:204-460; finite entrances, retained outgoing fade and door pulses.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private FrameClock? _tabTransitionTimer; // frame-locked like WPF's compositor, not a 16 ms timer
        private long _tabTransitionStart;
        private Control? _enteringTab;
        private TranslateTransform? _tabSlide;
        private (double X, double Y) _tabOffset;
        private readonly List<(Control Card, TranslateTransform? Slide)> _tabCards = new();
        internal Control? EnteringTab => _enteringTab;

        /// <summary>P08/P40 seam: the entrance reads real elapsed time from this clock; tests step it.</summary>
        internal TimeProvider TabClock { get; set; } = TimeProvider.System;

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
            _tabTransitionStart = TabClock.GetTimestamp();
            if (_tabTransitionTimer == null)
            {
                _tabTransitionTimer = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
                _tabTransitionTimer.Tick += (_, _) => TickTabTransition();
            }
            _tabTransitionTimer.Start();
        }

        private Control? _leavingTab;
        internal Control? LeavingTab => _leavingTab;

        // WPF ChromeFx.cs:303-339. Keep only the compositor drawing alive. The logical tab
        // stays hidden, so IsVisible-driven services stop once and never restart for the fade.
        // No tree traversal, bitmap allocation or synchronous render on the switch path.
        private void FadeLeavingTab(string target)
        {
            CompleteTabTransition();
            if (!AmbientFxCanvas.Env.AllowTransitions || ChromeFxNav.IsAirspaceTab(CurrentTab)
                || !TabPanels.TryGetValue(CurrentTab, out var oldName) || oldName == target
                || Named<Control>(oldName) is not { IsVisible: true } old
                || ElementComposition.GetElementVisual(old) is not { } visual) return;
            _leavingTab = old;
            old.IsHitTestVisible = false;
        }

        private void StartLeavingTabFade()
        {
            if (_leavingTab is not { } old || ElementComposition.GetElementVisual(old) is not { } visual) return;
            // The renderer first synchronizes the hidden tab. Install the overrides in the
            // same commit after that sync; an interrupted navigation invalidates this callback.
            visual.Compositor.RequestCompositionUpdate(() =>
            {
                if (_leavingTab != old) return;
                var visible = visual.Compositor.CreateBooleanKeyFrameAnimation();
                visible.Duration = TimeSpan.FromMilliseconds(100);
                visible.InsertKeyFrame(0, true);
                visible.InsertKeyFrame(1, false);
                var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
                opacity.Duration = visible.Duration;
                opacity.InsertKeyFrame(0, 1, new LinearEasing());
                opacity.InsertKeyFrame(1, 0, new LinearEasing());
                visual.StartAnimation("Visible", visible);
                visual.StartAnimation("Opacity", opacity);
            });
        }

        private void CompleteLeavingTab()
        {
            if (_leavingTab is not { } old) return;
            _leavingTab = null;
            old.IsHitTestVisible = true;
            if (ElementComposition.GetElementVisual(old) is not { } visual) return;
            // Replace finite clocks on the next commit, including rapid return before the
            // original 100 ms expires. Do not let an old fade hide a newly active page.
            visual.Compositor.RequestCompositionUpdate(() =>
            {
                if (_leavingTab == old) return;
                visual.StopAnimation("Visible");
                visual.StopAnimation("Opacity");
                visual.Visible = old.IsVisible;
                visual.Opacity = (float)old.Opacity;
            });
        }

        // P08: pointer tests settle the actual entrance before reading hit geometry. No wall-clock sleeps.
        internal void CompleteTabTransition() => StepTabTransition(620);

        /// <summary>One frame of the entrance at the clock's real elapsed time (the timer's tick).</summary>
        internal void TickTabTransition() => StepTabTransition(TabClock.GetElapsedTime(_tabTransitionStart).TotalMilliseconds);

        internal void StepTabTransition(double ms)
        {
            if (ms >= 100) CompleteLeavingTab();
            static double Ease(double elapsed, double duration)
            {
                var t = Math.Clamp(elapsed / duration, 0, 1);
                return 1 - (1 - t) * (1 - t);
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
            _tabCards.Clear();
            _tabSlide = null;
            _enteringTab = null;
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
