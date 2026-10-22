// PORTED from ConditioningControlPanel/MainWindow/MainWindow.MarqueeReads.cs (the Barnum interludes). Same as WPF: a
// loop clock keyed to one scroll period (45 s while the strip is parked) reports loops into the Core MarqueeReadCadence
// (every 4th loop, 2 min after launch, 3 min apart; fast mode 1 loop / 20 s); the gates are BannerPoolEnabled, the motion
// transitions gate and the strip on screen; a line comes from the Core BannerPoolService "reads" bucket, is split by
// MarqueeReadSplit and an act is drawn by MarqueeReadActs.PickId; the ticker pauses and fades out 280 ms, the act plays,
// the stage fades out 280 ms and the ticker resumes where it stopped. A rebuild, a hide or a detach cancels the act and
// hands the ticker back at once. Differs: the act is timed off a BeatLoop (P08/P82), not Storyboards and Task.Delay.
// ponytail: only the NeonWarmUp act, in its no-flicker form (WPF MarqueeReadEffects.cs:737 WarmUp's photosafe path),
// is on this head's table; GlitchHeal, NumberFirst, Teletype, Redaction, the flicker frames, the tag word's GlowBreath,
// the strip's DropShadow glow and the return SweepBannerSheen are marquee-reads-b.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Banner;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>The interludes on one marquee strip (WPF MainWindow.MarqueeReads.cs).</summary>
    internal sealed class MarqueeInterlude
    {
        /// <summary>WPF ReadFadeMs: ticker fade out / stage fade back.</summary>
        internal const double ReadFadeMs = 280;

        /// <summary>WPF ParkedLoopSeconds: the loop clock while the strip is parked static.</summary>
        internal const double ParkedLoopSeconds = 45;

        // WPF PossAnim.ToAsync waits the fade plus 20 ms of slack before the next step.
        private const double FadeStepMs = ReadFadeMs + 20;

        // WPF NeonWarmUpEffect (MarqueeReadEffects.cs:743-746).
        private const double WarmMs = 1800, HoldMs = 1200, TagMs = 700, TailMs = 1200;
        internal const double ActMs = WarmMs + HoldMs + TagMs + TailMs;

        /// <summary>The acts this head can play (marquee-reads-b adds the other four).</summary>
        private static readonly string[] Acts = { MarqueeReadActs.NeonWarmUp };

        private readonly MarqueeScroller _ticker;
        private readonly Control _canvas;
        private readonly Panel _host;
        private readonly BeatLoop _act;
        private readonly DispatcherTimer _loopTimer;
        private readonly BannerPoolService _pool = new();
        private readonly Random _rng = new();
        private readonly List<(TextBlock Word, double At, double Ms)> _words = new();
        private string? _lastAct;
        private bool _armed;

        public MarqueeInterlude(MarqueeScroller ticker, Control canvas, Panel host)
        {
            _ticker = ticker;
            _canvas = canvas;
            _host = host;
            bool fast = CoreSettings.Current.MarqueeReadDebugFast
                        || string.Equals(Environment.GetEnvironmentVariable("CCP_MARQUEE_READS_FAST"), "1", StringComparison.Ordinal);
            Cadence = new MarqueeReadCadence { Fast = fast };
            if (fast) Serilog.Log.Information("[MarqueeReads] fast cadence armed (desk mode)");
            _act = new BeatLoop(host, Step) { OnParked = Cancel };
            _loopTimer = new DispatcherTimer(DispatcherPriority.Background);
            _loopTimer.Tick += (_, _) => OnLoop();
            // WPF MarqueeRead_IsVisibleChanged stops the loop clock on hide and restarts it on show. Avalonia's
            // IsVisible is not effective, so watch the strip and its ancestors (the tab hosts toggle IsVisible).
            host.AttachedToVisualTree += (_, _) => Watch();
            host.DetachedFromVisualTree += (_, _) => Unwatch();
            if (host.IsAttachedToVisualTree()) Watch();
        }

        private Action? _unwatch;

        private void Watch()
        {
            Unwatch();
            var chain = _host.GetSelfAndVisualAncestors().ToArray();
            void On(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.IsVisibleProperty) SyncLoop();
            }
            foreach (var a in chain) a.PropertyChanged += On;
            _unwatch = () => { foreach (var a in chain) a.PropertyChanged -= On; };
        }

        private void Unwatch()
        {
            _unwatch?.Invoke();
            _unwatch = null;
        }

        /// <summary>The loop clock runs only while armed, idle and on screen.</summary>
        private void SyncLoop()
        {
            if (_armed && !IsPlaying && _host.IsEffectivelyVisible) _loopTimer.Start();
            else _loopTimer.Stop();
        }

        /// <summary>The rule (tests swap in one on a stepped clock).</summary>
        internal MarqueeReadCadence Cadence { get; set; }

        internal bool IsPlaying { get; private set; }

        /// <summary>The armed loop period, or null while the clock is stopped (tests).</summary>
        internal TimeSpan? LoopInterval => _loopTimer.IsEnabled ? _loopTimer.Interval : null;

        /// <summary>The line on stage, body and tag joined (tests).</summary>
        internal string StageText { get; private set; } = "";

        /// <summary>WPF ArmMarqueeReadClock: one tick per completed loop of the message now on the strip.</summary>
        public void Arm(double loopSeconds)
        {
            double seconds = loopSeconds > 0.5 ? loopSeconds : ParkedLoopSeconds;
            _loopTimer.Interval = TimeSpan.FromSeconds(Math.Min(600, seconds));
            _armed = true;
            SyncLoop();
        }

        /// <summary>WPF ShutdownMarqueeReads: no clock left pointing at a detached strip.</summary>
        public void Disarm()
        {
            _armed = false;
            _loopTimer.Stop();
        }

        /// <summary>WPF MarqueeReadLoop_Tick. A hidden strip counts no loops (WPF stops the timer on hide).</summary>
        internal void OnLoop()
        {
            try
            {
                if (IsPlaying || !_host.IsEffectivelyVisible) return;
                if (!Cadence.NoteLoop()) return;
                if (!CoreSettings.Current.BannerPoolEnabled || !AmbientFxCanvas.Env.AllowTransitions)
                {
                    Cadence.NoteSkipped();
                    return;
                }
                Play();
            }
            catch (Exception ex) { Serilog.Log.Debug("[MarqueeReads] loop tick: {E}", ex.Message); }
        }

        /// <summary>WPF RunMarqueeInterludeAsync: take the stage, play one act, give the ticker back.</summary>
        private void Play()
        {
            var line = _pool.NextLine(BannerPoolService.ReadsBucket);
            if (string.IsNullOrWhiteSpace(line)) { Cadence.NoteSkipped(); return; }

            var beats = MarqueeReadSplit.Split(line);
            var chosen = MarqueeReadActs.PickId(Acts, _lastAct, _ => !string.IsNullOrWhiteSpace(beats.Body), _rng.Next);
            if (chosen == null) { Cadence.NoteSkipped(); return; }

            IsPlaying = true;
            Cadence.NotePlayed();
            _lastAct = chosen;
            _loopTimer.Stop();
            Serilog.Log.Debug("[MarqueeReads] act {Act} (tag={HasTag})", chosen, beats.HasTag);

            BuildStrip(beats.Body, beats.Tag);
            StageText = (beats.Body + " " + beats.Tag).Trim();
            _host.Opacity = 0;
            _ticker.Pause();
            _act.Start();
        }

        /// <summary>WPF MarqueeReadStage.BuildStrip at the NeonWarmUp size: one word per cell, the tag's words
        /// built up front (hidden) so the strip is already at its final width when the body lands.</summary>
        private void BuildStrip(string body, string tag)
        {
            _host.Children.Clear();
            _words.Clear();
            const double size = 24;
            double gap = Math.Max(3, size * 0.34);
            var ink = _host.TryFindResource("PinkBrush", out var b) && b is IBrush brush ? brush : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var bodyWords = Words(body);
            for (int i = 0; i < bodyWords.Length; i++)
                Add(bodyWords[i], bodyWords.Length <= 1 ? 0 : WarmMs * 0.6 * i / (bodyWords.Length - 1), Math.Max(160, WarmMs * 0.4));
            foreach (var w in Words(tag)) Add(w, WarmMs + HoldMs, TagMs);

            _host.Children.Add(new Viewbox
            {
                Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 2, 14, 2),
            });

            void Add(string word, double at, double ms)
            {
                var block = new TextBlock
                {
                    Text = word.ToUpperInvariant(), FontFamily = new FontFamily("Impact, Arial Black, Segoe UI"), FontSize = size,
                    FontWeight = FontWeight.Bold, Foreground = ink, TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, gap, 0), Opacity = 0,
                };
                panel.Children.Add(block);
                _words.Add((block, at, ms));
            }
        }

        private static string[] Words(string text) => (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <summary>WPF PossAnim.EaseOut: CubicEase EaseOut.</summary>
        private static double Ease(double x) => 1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 3);

        /// <summary>The whole interlude as a function of elapsed time: ticker fade, the act, stage fade.</summary>
        private void Step(double t)
        {
            double ms = t * 1000;
            if (ms < FadeStepMs)
            {
                _canvas.Opacity = 1 - Ease(ms / ReadFadeMs);
                return;
            }
            _canvas.Opacity = 0;
            double a = ms - FadeStepMs;
            foreach (var (word, at, len) in _words) word.Opacity = Ease((a - at) / len);
            if (a < ActMs) { _host.Opacity = 1; return; }
            _host.Opacity = 1 - Ease((a - ActMs) / ReadFadeMs);
            if (a >= ActMs + FadeStepMs) Restore();
        }

        /// <summary>WPF CancelMarqueeInterlude: stop whatever is on stage and put the ticker back.</summary>
        public void Cancel()
        {
            if (IsPlaying) Restore();
        }

        /// <summary>WPF RestoreTicker: synchronous and unconditional, the cancel path included.</summary>
        private void Restore()
        {
            try
            {
                IsPlaying = false;
                _act.Stop();
                _host.Children.Clear();
                _words.Clear();
                _host.Opacity = 0;
                _canvas.Opacity = 1;
                _ticker.Resume();
                SyncLoop();
            }
            catch (Exception ex) { Serilog.Log.Debug("[MarqueeReads] restore: {E}", ex.Message); }
        }
    }
}
