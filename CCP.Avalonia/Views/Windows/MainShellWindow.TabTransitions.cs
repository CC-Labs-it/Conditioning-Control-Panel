// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs:186-260 (AnimateTabIn, the
// crossfade half) and MainWindow.TabNavigation.cs:822-875 (the first-visit door-header pulse), with
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
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using ConditioningControlPanel.Services;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private readonly List<Control> _staggeredTabCards = new();
        private readonly List<(Control Target, ITransform? Original)> _entranceTransforms = new();
        internal IReadOnlyList<Control> StaggeredTabCards => _staggeredTabCards;

        // Explicit settle seam: gesture tests can wait for the entrance without wall-clock sleeps.
        internal void SettleTabEntrance()
        {
            var entrance = _tabEntrance;
            _tabEntrance = null;
            entrance?.Cancel();
            entrance?.Dispose();
            foreach (var (target, original) in _entranceTransforms) target.RenderTransform = original;
            foreach (var card in _staggeredTabCards) card.Opacity = 1;
            _entranceTransforms.Clear();
            _staggeredTabCards.Clear();
            if (EnteringTab is { } tab) tab.Opacity = 1;
            EnteringTab = null;
            ClearOutgoingTab();
        }

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
        private void AnimateTabIn(Control? tab, string fromKey, string key)
        {
            // Outgoing snapshot belongs to this navigation, not the previous entrance.
            var outgoing = _outgoingTab;
            _outgoingTab = null;
            SettleTabEntrance();
            _outgoingTab = outgoing;
            if (tab == null) return;
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) { tab.Opacity = 1; return; }
                var run = new CancellationTokenSource();
                _tabEntrance = run;
                EnteringTab = tab;
                _ = RunEntrance(tab, run, fromKey, key);
            }
            catch (Exception ex)
            {
                Log.Debug("AnimateTabIn: {E}", ex.Message);
                SettleTabEntrance();
                tab.Opacity = 1;
            }
        }

        private async Task RunEntrance(Control tab, CancellationTokenSource run, string fromKey, string key)
        {
            try
            {
                var tasks = new List<Task>();
                tasks.Add(new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(TabFadeInMs),
                    Easing = new QuadraticEaseOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                    },
                }.RunAsync(tab, run.Token));
                if (AmbientFxCanvas.Env.Level == MotionLevel.Full && !TabEntranceRule.IsAirspaceTab(key))
                {
                    var (x, y) = TabEntranceRule.EntranceOffset(fromKey, key, 12);
                    tasks.Add(SlideEntrance(tab, x, y, 200, 0, run.Token));
                    var cards = FindStaggerTargets(tab);
                    _staggeredTabCards.AddRange(cards);
                    for (int i = 0; i < cards.Count; i++)
                    {
                        int delay = 40 * Math.Min(i, 6);
                        tasks.Add(SlideEntrance(cards[i], 0, 10, 260, delay, run.Token));
                        tasks.Add(new Animation
                        {
                            Delay = TimeSpan.FromMilliseconds(delay),
                            Duration = TimeSpan.FromMilliseconds(220),
                            FillMode = FillMode.Backward,
                            Easing = new QuadraticEaseOut(),
                            Children =
                            {
                                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                            },
                        }.RunAsync(cards[i], run.Token));
                    }
                }
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("Tab entrance: {E}", ex.Message); }
            finally { if (_tabEntrance == run) SettleTabEntrance(); }
        }

        private Task SlideEntrance(Control target, double x, double y, int ms, int delay, CancellationToken token)
        {
            // Authored transforms belong to the page. Animate the Visual, never a Transform.
            if (target.RenderTransform is { } transform && !transform.Value.IsIdentity) return Task.CompletedTask;
            if (target.RenderTransform is TransformGroup or ScaleTransform or RotateTransform or SkewTransform)
                return Task.CompletedTask;
            _entranceTransforms.Add((target, target.RenderTransform));
            var from = TransformOperations.Parse(FormattableString.Invariant($"translate({x}px, {y}px)"));
            target.RenderTransform = from;
            return new Animation
            {
                Delay = TimeSpan.FromMilliseconds(delay),
                Duration = TimeSpan.FromMilliseconds(ms),
                FillMode = FillMode.Both,
                Easing = new QuadraticEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.RenderTransformProperty, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.RenderTransformProperty, TransformOperations.Parse("translate(0px, 0px)")) } },
                },
            }.RunAsync(target, token);
        }

        internal static List<Control> FindStaggerTargets(Control? node)
        {
            for (int depth = 0; depth < 8 && node != null; depth++)
            {
                switch (node)
                {
                    case ContentControl { Content: Control content }: node = content; continue;
                    case Decorator { Child: { } child }: node = child; continue;
                    case Panel panel:
                        var children = panel.Children.Where(c => c.IsVisible && c.Opacity > 0.99).ToList();
                        if (children.Count == 1) { node = children[0]; continue; }
                        return children.Take(8).ToList();
                    default: return new();
                }
            }
            return new();
        }

        // ponytail: fade a snapshot instead of re-showing the live page: IsVisible owns its timers.
        private (Panel Parent, Image Image, RenderTargetBitmap Bitmap, CancellationTokenSource Run)? _outgoingTab;
        private void PrepareOutgoingTab(Control? outgoing, Control? incoming, string fromKey)
        {
            SettleTabEntrance();
            if (!AmbientFxCanvas.Env.AllowTransitions || TabEntranceRule.IsAirspaceTab(fromKey)
                || outgoing == null || ReferenceEquals(outgoing, incoming)
                || outgoing.Parent is not Panel parent || outgoing.Bounds.Width < 1 || outgoing.Bounds.Height < 1) return;
            RenderTargetBitmap? bitmap = null;
            try
            {
                var bounds = outgoing.Bounds;
                bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height)));
                bitmap.Render(outgoing);
                var image = new Image
                {
                    Source = bitmap, Width = bounds.Width, Height = bounds.Height,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                    Margin = new Thickness(bounds.X, bounds.Y, 0, 0), IsHitTestVisible = false,
                };
                var run = new CancellationTokenSource();
                parent.Children.Add(image);
                _outgoingTab = (parent, image, bitmap, run);
                _ = FadeOutgoingSnapshot(image, run);
            }
            catch (Exception ex) { bitmap?.Dispose(); Log.Debug("Outgoing tab snapshot: {E}", ex.Message); }
        }

        private async Task FadeOutgoingSnapshot(Image image, CancellationTokenSource run)
        {
            try
            {
                await new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(100),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                    },
                }.RunAsync(image, run.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("Outgoing tab fade: {E}", ex.Message); }
            finally { if (_outgoingTab?.Run == run) ClearOutgoingTab(); }
        }

        private void ClearOutgoingTab()
        {
            var outgoing = _outgoingTab;
            _outgoingTab = null;
            if (outgoing is not { } item) return;
            item.Run.Cancel();
            item.Parent.Children.Remove(item.Image);
            item.Image.Source = null;
            item.Bitmap.Dispose();
            item.Run.Dispose();
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
