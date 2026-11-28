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
using Avalonia.Rendering.Composition.Animations;
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
        // The entrance runs on the compositor, like WPF's storyboards (ChromeFx.cs:235-252) run on its render
        // thread: a UI-thread clock committed a batch every frame, and on a CPU-only renderer (~110 ms a frame)
        // the next tab click waited behind that batch (perf-bench handoff p95, 2026-11-15). This timer only
        // fires the 100 ms and 620 ms cleanups; it draws nothing.
        private DispatcherTimer? _tabTransitionTimer;
        private long _tabTransitionStart;
        private Control? _enteringTab;
        private int _entranceRun;
        internal Control? EnteringTab => _enteringTab;

        /// <summary>One compositor animation of the entrance: <paramref name="Property"/> runs from
        /// (<paramref name="FromX"/>, <paramref name="FromY"/>) to rest (Opacity 1, Translation 0) with WPF's
        /// QuadraticEase EaseOut, after <paramref name="DelayMs"/> at rest (WPF BeginTime).</summary>
        internal readonly record struct EntranceTrack(Control Target, string Property, double FromX, double FromY, int DelayMs, int DurationMs)
        {
            /// <summary>The value this track draws <paramref name="ms"/> after the switch (tests).</summary>
            internal double At(double ms, bool y = false)
            {
                var from = y ? FromY : FromX;
                var rest = Property == "Opacity" ? 1 : 0;
                if (ms < DelayMs) return rest;
                return from + (rest - from) * new QuadraticEaseOut().Ease(Math.Clamp((ms - DelayMs) / DurationMs, 0, 1));
            }
        }

        /// <summary>The animations the current entrance handed to the compositor (tests read them).</summary>
        internal List<EntranceTrack> EntranceTracks { get; } = new();

        /// <summary>P08/P40 seam: the cleanup milestones read elapsed time from this clock; tests step it.</summary>
        internal TimeProvider TabClock { get; set; } = TimeProvider.System;

        private void AnimateTabIn(Control? tab, string key)
        {
            if (tab == null) return;
            tab.IsHitTestVisible = true;
            tab.Opacity = 1;
            if (!AmbientFxCanvas.Env.AllowTransitions) return;
            _enteringTab = tab;
            EntranceTracks.Clear();
            // WPF ChromeFx.cs:235-239: a 200 ms fade at every level but Off.
            EntranceTracks.Add(new(tab, "Opacity", 0, 0, 0, 200));
            if (AmbientFxCanvas.Env.Level == MotionLevel.Full && !ChromeFxNav.IsAirspaceTab(key))
            {
                // WPF ChromeFx.cs:267-291: 12 px along the rail's direction, 200 ms; an authored transform is left alone.
                var (x, y) = ChromeFxNav.EntranceOffset(CurrentTab, key, 12);
                if (Slides(tab)) EntranceTracks.Add(new(tab, "Translation", x, y, 0, 200));
                // WPF MotionFx.cs:106-138 StaggerIn: 40 ms slots capped at 6, 220 ms fade, 10 px 260 ms rise.
                var cards = FindStaggerTargets(tab);
                for (var i = 0; i < cards.Count; i++)
                {
                    var delay = 40 * Math.Min(i, 6);
                    EntranceTracks.Add(new(cards[i], "Opacity", 0, 0, delay, 220));
                    if (Slides(cards[i])) EntranceTracks.Add(new(cards[i], "Translation", 0, 10, delay, 260));
                }
            }
            var run = ++_entranceRun;
            var tracks = EntranceTracks.ToArray();
            // Same commit as the renderer's sync of the newly visible tab (see StartLeavingTabFade).
            ElementComposition.GetElementVisual(tab)?.Compositor.RequestCompositionUpdate(() =>
            {
                if (run != _entranceRun) return;
                foreach (var t in tracks) StartTrack(t);
            });
            _tabTransitionStart = TabClock.GetTimestamp();
            ArmTabTransition(_leavingTab != null ? 100 : 620);
        }

        private static bool Slides(Control c) => c.RenderTransform is null or TranslateTransform;

        private static void StartTrack(EntranceTrack t)
        {
            if (ElementComposition.GetElementVisual(t.Target) is not { } visual) return;
            var compositor = visual.Compositor;
            KeyFrameAnimation animation;
            // WPF BeginTime holds the base value through the delay. Avalonia's SetInitialValueAfterDelay holds the
            // animation's own empty cache instead (opacity 0), so a delayed track leads with a rest key frame that
            // SetInitialValueBeforeDelay draws until the delay ends, then jumps to the start value.
            var held = t.DelayMs > 0;
            if (t.Property == "Opacity")
            {
                var fade = compositor.CreateScalarKeyFrameAnimation();
                if (held) fade.InsertKeyFrame(0, 1);
                fade.InsertKeyFrame(0, (float)t.FromX);
                fade.InsertKeyFrame(1, 1, new QuadraticEaseOut());
                animation = fade;
            }
            else
            {
                var slide = compositor.CreateVector3DKeyFrameAnimation();
                if (held) slide.InsertKeyFrame(0, default);
                slide.InsertKeyFrame(0, new Vector3D(t.FromX, t.FromY, 0));
                slide.InsertKeyFrame(1, default, new QuadraticEaseOut());
                animation = slide;
            }
            animation.Duration = TimeSpan.FromMilliseconds(t.DurationMs);
            // The frame that starts an animation samples it at exactly 0 ms, where Avalonia 12.1 keeps the current
            // value: one tick of delay makes that frame draw key frame 0, not a flash of the tab at rest.
            animation.DelayTime = TimeSpan.FromMilliseconds(t.DelayMs) + TimeSpan.FromTicks(1);
            animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation(t.Property, animation);
        }

        private static void SettleTrack(EntranceTrack t)
        {
            if (ElementComposition.GetElementVisual(t.Target) is not { } visual) return;
            if (t.Property == "Opacity") SetOpacityNow(visual, (float)t.Target.Opacity);
            else
            {
                visual.Translation = new Vector3D(1, 0, 0);
                visual.Translation = default;
            }
        }

        // A direct value ends a compositor animation where it is, but the client skips a write of the value it
        // already holds (an animation never changes the client copy), so write another value first.
        private static void SetOpacityNow(CompositionVisual visual, float opacity)
        {
            visual.Opacity = opacity == 0 ? 1 : 0;
            visual.Opacity = opacity;
        }

        private void ArmTabTransition(double ms)
        {
            if (_tabTransitionTimer == null)
            {
                _tabTransitionTimer = new DispatcherTimer();
                _tabTransitionTimer.Tick += (_, _) => TickTabTransition();
            }
            _tabTransitionTimer.Stop();
            _tabTransitionTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
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
            // --render/--render-all photograph one frame: never leave a half-faded old tab in it.
            if (RenderProof.Rendering || !AmbientFxCanvas.Env.AllowTransitions || ChromeFxNav.IsAirspaceTab(CurrentTab)
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
                // At exactly zero Avalonia samples the hidden base value. Hold the first
                // key frame through a one-tick delay so the outgoing tab draws immediately.
                visible.DelayTime = TimeSpan.FromTicks(1);
                visible.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
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
                visual.Visible = !old.IsVisible; // stopped mid-fade, the server keeps the fade's value (SetOpacityNow)
                visual.Visible = old.IsVisible;
                SetOpacityNow(visual, (float)old.Opacity);
            });
        }

        // P08: pointer tests settle the actual entrance before reading hit geometry. No wall-clock sleeps.
        internal void CompleteTabTransition() => StepTabTransition(620);

        /// <summary>The cleanup timer: the leaving tab at 100 ms, the entrance bookkeeping at 620 ms, at the clock's
        /// real elapsed time. The compositor has already landed every value, so nothing is written here.</summary>
        internal void TickTabTransition()
        {
            _tabTransitionTimer?.Stop();
            var ms = TabClock.GetElapsedTime(_tabTransitionStart).TotalMilliseconds;
            if (ms >= 100) CompleteLeavingTab();
            if (ms >= 620) { EndEntrance(settle: false); return; }
            if (_enteringTab != null || _leavingTab != null) ArmTabTransition((_leavingTab != null && ms < 100 ? 100 : 620) - ms);
        }

        /// <summary>Jumps the transition to <paramref name="ms"/>: 100 ends the leaving fade, 620 lands the entrance
        /// at rest now (a new switch, Close, a render proof, pointer tests).</summary>
        internal void StepTabTransition(double ms)
        {
            if (ms >= 100) CompleteLeavingTab();
            if (ms >= 620) EndEntrance(settle: true);
        }

        private void EndEntrance(bool settle)
        {
            _tabTransitionTimer?.Stop();
            _entranceRun++; // a start still waiting for its commit is void
            if (settle) foreach (var t in EntranceTracks) SettleTrack(t);
            EntranceTracks.Clear();
            _enteringTab = null;
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
        private IDisposable? _programsFirstVisitDelay;
        private IDisposable? _deeperFirstVisitDelay;

        internal void StopProgramsAnnouncement()
        {
            _programsFirstVisitDelay?.Dispose();
            _programsFirstVisitDelay = null;
            StopNavDoorHeaderPulse("programs");
        }

        /// <summary>Stepped-clock seam (P08): tests capture the 1.2 s arming instead of waiting it out.</summary>
        internal Func<Action, TimeSpan, IDisposable> ScheduleOnce { get; set; } =
            (action, delay) => DispatcherTimer.RunOnce(action, delay);

        /// <summary>WPF MainWindow.xaml.cs:3882-3918, on load: 1.2 s later the Deeper and Programs
        /// rail rows announce a page the user has not opened yet. Opened fires on every Show, so armed once.</summary>
        private void InitializeFirstVisitPulses()
        {
            void MotionChanged()
            {
                if (!AmbientFxCanvas.Env.AllowAmbientLoops) StopProgramsAnnouncement();
            }
            AmbientFxCanvas.Env.MotionGateChanged += MotionChanged;
            PropertyChanged += (_, e) =>
            {
                if ((e.Property == IsVisibleProperty && !IsVisible) ||
                    (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)) StopProgramsAnnouncement();
            };
            Opened += (_, _) =>
            {
                if (_firstVisitPulsesArmed) return;
                // --render/--render-all capture one frame. On a slow host (Linux/WSL Skia) the 1.2 s
                // timer fires inside CaptureRenderedFrame and photographs Play/You mid-dip; draw at rest.
                if (RenderProof.Rendering) return;
                _firstVisitPulsesArmed = true;
                // WPF MainWindow.xaml.cs:3885-3913: independent startup delays.
                _deeperFirstVisitDelay = ScheduleOnce(StartDeeperFirstVisitPulse, TimeSpan.FromMilliseconds(1200));
                _programsFirstVisitDelay = ScheduleOnce(StartProgramsFirstVisitPulse, TimeSpan.FromMilliseconds(1200));
            };
            // Nothing of this window's motion may outlive it (P65): the pending start, the rows, the entrance.
            Closed += (_, _) =>
            {
                _programsFirstVisitDelay?.Dispose();
                _deeperFirstVisitDelay?.Dispose();
                AmbientFxCanvas.Env.MotionGateChanged -= MotionChanged;
                foreach (var key in _navHeaderPulses.Keys.ToList()) StopNavDoorHeaderPulse(key);
                CompleteTabTransition();
            };
        }

        /// <summary>The two independent first-visit checks (WPF xaml.cs:3885 Deeper, :3906 Programs).</summary>
        internal void StartFirstVisitPulses()
        {
            StartDeeperFirstVisitPulse();
            StartProgramsFirstVisitPulse();
        }

        private void StartDeeperFirstVisitPulse()
        {
            var s = CoreSettings.Current;
            if (s.EnableDeeper && !s.HasSeenDeeperTab) StartNavDoorHeaderPulse("deeper");
        }

        private void StartProgramsFirstVisitPulse()
        {
            var s = CoreSettings.Current;
            // WPF StartProgramsTabPulse (ProgramsTab.cs:54): a decorative loop, so it goes with the ambient clocks.
            if (IsVisible && WindowState != WindowState.Minimized && !s.HasSeenProgramsTab && AmbientFxCanvas.Env.AllowAmbientLoops) StartNavDoorHeaderPulse("programs");
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
