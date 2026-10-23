// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs:186-260 (AnimateTabIn, the
// crossfade half) and MainWindow.TabNavigation.cs:822-875 (the first-visit door-header pulse), with
// the deferred startup that arms it (MainWindow.xaml.cs:3795-3830). ShowTab calls AnimateTabIn for
// every real tab. Durations, easings and the Off / Reduced split are WPF's, verbatim.
//
// NOT YET, named in the parity row as wire-shell-tab-navigation-b-next:
//   * Full motion's 12 px directional slide (SlideTabIn, ChromeFxNav.EntranceOffset) and the card
//     stagger (StaggerTabCards / FindStaggerTargets). Both MOVE hit geometry for 200-460 ms after a
//     ShowTab; a first port broke every headless test that clicks or hovers right after navigating
//     (AssetsTabTests hover, CompanionPersonalityZoneTests chips; gate run 39812584919). They need a
//     test seam that settles entrances first. Until then Full gets WPF's Reduced entrance (crossfade).
//   * The 100 ms outgoing fade (FadeOutgoingTab): it re-shows the tab just left, and on this head
//     every tab's timers, refreshes and FX follow IsVisible (P01), so the leaving tab is cut instead.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const int TabFadeInMs = 200;      // WPF ChromeFx.cs:38

        /// <summary>The entrance in flight; the next navigation cancels it, so a half-faded tab
        /// never keeps an animated value after the user has moved on.</summary>
        private CancellationTokenSource? _tabEntrance;

        /// <summary>The tab the last entrance animated (test seam).</summary>
        internal Control? EnteringTab { get; private set; }

        /// <summary>
        /// WPF AnimateTabIn (ChromeFx.cs:204): Off -> instant swap, no clocks; otherwise the
        /// incoming tab fades in over 200 ms, QuadraticEaseOut. Never throws.
        /// </summary>
        private void AnimateTabIn(Control? tab)
        {
            _tabEntrance?.Cancel();
            _tabEntrance = null;
            EnteringTab = null;
            if (tab == null) return;
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) { tab.Opacity = 1; return; }
                var run = new CancellationTokenSource();
                _tabEntrance = run;
                EnteringTab = tab;
                _ = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(TabFadeInMs),
                    Easing = new QuadraticEaseOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                    },
                }.RunAsync(tab, run.Token);
            }
            catch (Exception ex)
            {
                Log.Debug("AnimateTabIn: {E}", ex.Message);
                try { tab.Opacity = 1; } catch { }
            }
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
                _tabEntrance?.Cancel();
                _tabEntrance = null;
                EnteringTab = null;
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
