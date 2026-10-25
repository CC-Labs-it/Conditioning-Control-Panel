// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs:186-260 (AnimateTabIn, the
// entrance and outgoing fade) and MainWindow.TabNavigation.cs:822-875 (the first-visit door-header pulse), with
// the deferred startup that arms it (MainWindow.xaml.cs:3795-3830). ShowTab calls AnimateTabIn for
// every real tab. Durations, easings and the Off / Reduced split are WPF's, verbatim.
//
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
        /// <summary>The tab the last entrance animated (test seam).</summary>
        internal Control? EnteringTab { get; private set; }

        /// <summary>
        /// WPF AnimateTabIn (ChromeFx.cs:204): Off -> instant swap, no clocks; otherwise the
        /// incoming tab fades in over 200 ms, QuadraticEaseOut. Never throws.
        /// </summary>
        private void AnimateTabIn(Control? tab)
        {
            ResetEntranceParts();
            if (tab == null) { ClearLeavingFrame(); return; }
            if (!AmbientFxCanvas.Env.AllowTransitions) { ClearLeavingFrame(); return; }
            EnteringTab = tab;
            _entranceWatch.Restart();
            _entranceParts.Add((tab, TranslateForEntrance(tab), 0, 0, 0, 200));
            if (AmbientFxCanvas.Env.Level == MotionLevel.Full && !Services.UI.ChromeFxNav.IsAirspaceTab(_incomingTabKey))
            {
                var offset = Services.UI.ChromeFxNav.EntranceOffset(CurrentTab, _incomingTabKey, 12);
                var first = _entranceParts[0];
                _entranceParts[0] = first with { X = offset.X, Y = offset.Y };
                int i = 0;
                foreach (var card in FindEntranceCards(tab))
                    _entranceParts.Add((card, TranslateForEntrance(card), 0, 10, 40 * Math.Min(i++, 6), 260));
            }
            AdvanceTabEntrance(TimeSpan.Zero);
            // Frame-locked (WPF storyboards ride CompositionTarget.Rendering); stops once settled.
            if (_entranceTimer == null)
            {
                _entranceTimer = new global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock(this);
                _entranceTimer.Tick += (_, _) => AdvanceTabEntrance(_entranceWatch.Elapsed);
            }
            _entranceTimer.Start();
        }

        private readonly System.Diagnostics.Stopwatch _entranceWatch = new();
        private global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock? _entranceTimer;
        private string? _incomingTabKey;
        private readonly List<(Control Item, global::Avalonia.Media.TranslateTransform? Slide, double X, double Y, double Delay, double Duration)> _entranceParts = new();
        private global::Avalonia.Controls.Image? _leavingFrame;
        private global::Avalonia.Media.Imaging.RenderTargetBitmap? _leavingBitmap;
        internal bool HasLeavingTabFrame => _leavingFrame != null;
        internal PixelSize? LeavingFramePixelSize => _leavingBitmap?.PixelSize;
        internal double LastLeavingSnapshotMs { get; private set; }
        internal int EntranceCardCount => Math.Max(0, _entranceParts.Count - 1);

        // WPF FadeOutgoingTab, adapted: fade a frozen frame rather than re-showing a live tab.
        // Re-showing fires IsVisible hooks here and restarts the departing page's services.
        // ponytail: a tab not measured yet has no frame to fade; cut it, as for native airspace.
        private void CaptureLeavingTab(string incomingKey)
        {
            ClearLeavingFrame();
            _incomingTabKey = incomingKey;
            if (!AmbientFxCanvas.Env.AllowTransitions || Services.UI.ChromeFxNav.IsAirspaceTab(CurrentTab)) return;
            if (!TabPanels.TryGetValue(CurrentTab, out var oldName) || !TabPanels.TryGetValue(incomingKey, out var newName)
                || oldName == newName || Named<Control>(oldName) is not { IsVisible: true } old
                || old.Parent is not Grid host || old.Bounds.Width <= 0 || old.Bounds.Height <= 0) return;
            try
            {
                // Device pixels at the window's scaling, so the fading frame stays sharp on HiDPI.
                double scale = RenderScaling > 0 ? RenderScaling : 1;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                _leavingBitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
                    SnapshotPixels(old.Bounds.Size, scale), new Vector(96 * scale, 96 * scale));
                _leavingBitmap.Render(old);
                LastLeavingSnapshotMs = watch.Elapsed.TotalMilliseconds;
                Log.Debug("Tab snapshot {Tab}: {Px} in {Ms:F1} ms", CurrentTab, _leavingBitmap.PixelSize, LastLeavingSnapshotMs);
                _leavingFrame = new global::Avalonia.Controls.Image { Source = _leavingBitmap, IsHitTestVisible = false,
                    Margin = old.Margin, Stretch = global::Avalonia.Media.Stretch.Fill };
                host.Children.Add(_leavingFrame);
            }
            catch (Exception ex) { Log.Debug("Tab snapshot: {E}", ex.Message); ClearLeavingFrame(); }
        }

        internal static PixelSize SnapshotPixels(Size dips, double scale) =>
            new((int)Math.Ceiling(dips.Width * scale), (int)Math.Ceiling(dips.Height * scale));

        private void ClearLeavingFrame()
        {
            if (_leavingFrame?.Parent is Panel host) host.Children.Remove(_leavingFrame);
            _leavingFrame = null;
            _leavingBitmap?.Dispose();
            _leavingBitmap = null;
        }

        // Explicit elapsed-time seam lets pointer tests settle geometry without sleeping or
        // disabling Full motion. The same finite clock drives production and the tests.
        internal void AdvanceTabEntrance(TimeSpan elapsed)
        {
            double ms = elapsed.TotalMilliseconds;
            if (_leavingFrame != null)
            {
                _leavingFrame.Opacity = Math.Clamp(1 - ms / 100, 0, 1);
                if (ms >= 100) ClearLeavingFrame();
            }
            foreach (var part in _entranceParts)
            {
                double t = Math.Clamp((ms - part.Delay) / part.Duration, 0, 1);
                double eased = 1 - (1 - t) * (1 - t);
                // WPF card fade is 220ms, rise 260ms; the tab fade/slide is 200ms.
                double fadeT = Math.Clamp((ms - part.Delay) / (part.Duration == 260 ? 220 : 200), 0, 1);
                part.Item.Opacity = 1 - (1 - fadeT) * (1 - fadeT);
                if (part.Slide != null) { part.Slide.X = part.X * (1 - eased); part.Slide.Y = part.Y * (1 - eased); }
            }
            if (ms >= 620) SettleTabEntrance();
        }

        internal void SettleTabEntrance()
        {
            ResetEntranceParts();
            ClearLeavingFrame();
        }

        private void ResetEntranceParts()
        {
            _entranceTimer?.Stop();
            foreach (var part in _entranceParts)
            {
                part.Item.Opacity = 1;
                if (part.Slide != null) part.Slide.X = part.Slide.Y = 0;
            }
            _entranceParts.Clear();
            EnteringTab = null;
        }

        private static global::Avalonia.Media.TranslateTransform? TranslateForEntrance(Control item)
        {
            if (item.RenderTransform is global::Avalonia.Media.TranslateTransform t) return t;
            if (item.RenderTransform != null) return null; // preserve authored transforms
            var slide = new global::Avalonia.Media.TranslateTransform();
            item.RenderTransform = slide;
            return slide;
        }

        internal static IReadOnlyList<Control> FindEntranceCards(Control? node)
        {
            for (int depth = 0; depth < 8 && node != null; depth++)
            {
                switch (node)
                {
                    case ContentControl cc when cc.Content is Control inner: node = inner; break;
                    case Decorator dec when dec.Child != null: node = dec.Child; break;
                    case Panel panel:
                        var children = panel.Children.Where(c => c.IsVisible && c.Opacity > 0.99).ToList();
                        if (children.Count == 1) { node = children[0]; break; }
                        return children.Take(8).ToList();
                    default: return Array.Empty<Control>();
                }
            }
            return Array.Empty<Control>();
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
                SettleTabEntrance();
                ClearLeavingFrame();
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
